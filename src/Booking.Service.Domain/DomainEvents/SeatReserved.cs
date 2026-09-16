using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Domain;

namespace Booking.Service.Domain.DomainEvents;

public sealed record SeatReserved(
    Guid BookingId,
    EventId EventId,
    CustomerId CustomerId,
    SeatNumber SeatNumber,
    DateTime OccurredOnUtc) : DomainEvent(OccurredOnUtc);
