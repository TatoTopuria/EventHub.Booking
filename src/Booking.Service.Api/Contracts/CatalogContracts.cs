namespace Booking.Service.Api.Contracts;

public sealed record CatalogEventDetailsResponse(
    Guid EventId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Organizer,
    string Status,
    IReadOnlyCollection<string> Seats,
    IReadOnlyCollection<string> ReservedSeats);
