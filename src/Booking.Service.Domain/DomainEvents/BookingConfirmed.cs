using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Domain;

namespace Booking.Service.Domain.DomainEvents;

public sealed record BookingConfirmed(
    Guid BookingId,
    EventId EventId,
    CustomerId CustomerId,
    DateTime OccurredOnUtc) : DomainEvent(OccurredOnUtc);
