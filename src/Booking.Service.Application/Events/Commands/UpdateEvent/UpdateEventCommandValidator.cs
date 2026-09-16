using FluentValidation;

namespace Booking.Service.Application.Events.Commands.UpdateEvent;

/// <summary>
/// Shape validation for <see cref="UpdateEventCommand"/>. Mirrors the aggregate's invariants so
/// callers get clean field-level 400s instead of generic aggregate errors surfaced as 500s.
/// </summary>
/// <remarks>
/// Patch semantics: every field is nullable, and a request with no mutable fields populated is
/// a no-op rather than an error — that matches the aggregate's NoOp branches and keeps idempotent
/// retries safe. The handler still calls into the aggregate which is the source of truth for the
/// real invariants (schedule ordering, organizer not blank, cancelled-event lock-down).
/// </remarks>
public sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    private const int OrganizerMaxLength = 200;

    public UpdateEventCommandValidator()
    {
        RuleFor(command => command.EventId)
            .NotEqual(Guid.Empty)
            .WithMessage("EventId is required.");

        // Either both schedule fields land together (full reschedule) OR one alone (partial). What
        // we cannot accept is a non-monotonic pair when both are supplied; the aggregate would
        // refuse but the validator surfaces a friendlier field-level error first.
        When(command => command.StartsAtUtc.HasValue && command.EndsAtUtc.HasValue, () =>
        {
            RuleFor(command => command.StartsAtUtc!.Value)
                .LessThan(command => command.EndsAtUtc!.Value)
                .WithMessage("StartsAtUtc must be strictly before EndsAtUtc.");
        });

        When(command => command.Organizer is not null, () =>
        {
            RuleFor(command => command.Organizer!)
                .NotEmpty()
                .WithMessage("Organizer cannot be blank when provided.")
                .MaximumLength(OrganizerMaxLength);
        });
    }
}
