using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Snapshot of the inputs a reservation request needs to be validated against.
/// </summary>
/// <param name="EventAggregate">
/// The event being booked, already loaded from the repository. The chain runs read-only validations,
/// so handing it the aggregate (rather than a query interface) keeps each handler simple.
/// </param>
/// <param name="CustomerId">The customer attempting the reservation.</param>
/// <param name="SeatNumber">The seat the customer wants to reserve.</param>
public sealed record BookingValidationContext(
    Event EventAggregate,
    CustomerId CustomerId,
    SeatNumber SeatNumber);
