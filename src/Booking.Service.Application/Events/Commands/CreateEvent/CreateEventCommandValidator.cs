using FluentValidation;

namespace Booking.Service.Application.Events.Commands.CreateEvent;

/// <summary>
/// Shape validation for <see cref="CreateEventCommand"/>. Runs ahead of the aggregate so callers
/// see field-level 400 problem-details ("Seats must be unique") instead of generic aggregate
/// errors ("At least one seat is required") which surface as opaque 500s through the middleware.
/// </summary>
/// <remarks>
/// The Event aggregate still enforces every one of these invariants — this validator only changes
/// the error <em>presentation</em>, not the rule. Aggregate-enforced invariants remain the source
/// of truth; the validator gives the FluentValidation pipeline a chance to surface them with field
/// paths and stable error messages.
/// </remarks>
public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    private const int OrganizerMaxLength = 200;
    private const int SeatMaxLength = 20;
    private const int SeatsHardCap = 1000;

    public CreateEventCommandValidator()
    {
        RuleFor(command => command.Organizer)
            .NotEmpty()
            .MaximumLength(OrganizerMaxLength);

        RuleFor(command => command.StartsAtUtc)
            .LessThan(command => command.EndsAtUtc)
            .WithMessage("StartsAtUtc must be strictly before EndsAtUtc.");

        RuleFor(command => command.Seats)
            .NotNull()
            .Must(seats => seats is { Count: > 0 })
                .WithMessage("At least one seat is required.")
            .Must(seats => seats == null || seats.Count <= SeatsHardCap)
                .WithMessage($"At most {SeatsHardCap} seats are allowed per event.")
            .Must(seats => seats == null || seats.All(seat => !string.IsNullOrWhiteSpace(seat)))
                .WithMessage("Seat numbers cannot be blank.")
            .Must(seats => seats == null || seats.All(seat => seat is { Length: <= SeatMaxLength }))
                .WithMessage($"Seat numbers cannot exceed {SeatMaxLength} characters.")
            .Must(seats => seats == null || HasUniqueSeats(seats))
                .WithMessage("Seat numbers must be unique within the request.");
    }

    private static bool HasUniqueSeats(IReadOnlyCollection<string> seats)
    {
        // Mirrors SeatNumber.Create's normalization (trim + uppercase) so a request that sends
        // "A1" and "a1" is rejected as a duplicate at the validation layer too — matching the
        // domain's equality semantics.
        var nonBlank = seats.Where(seat => !string.IsNullOrWhiteSpace(seat)).ToArray();
        var normalized = nonBlank.Select(seat => seat.Trim().ToUpperInvariant());
        return normalized.Distinct(StringComparer.Ordinal).Count() == nonBlank.Length;
    }
}
