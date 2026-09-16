namespace Booking.Service.Api.Contracts;

public sealed record CreateEventRequest(
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Organizer,
    IReadOnlyCollection<string> Seats);

/// <summary>
/// Payload for <c>PATCH /api/events/{eventId}</c>. Every field is nullable so callers can patch
/// the schedule, the organizer, or both — leaving the others alone. Matches the patch semantics
/// of <c>UpdateEventCommand</c>.
/// </summary>
public sealed record UpdateEventRequest(
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    string? Organizer);
