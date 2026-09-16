using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Composes the canonical booking-validation chain:
/// <list type="number">
///   <item><see cref="EventPublishedHandler"/> — cheapest check, gates everything else.</item>
///   <item><see cref="SeatExistsHandler"/> — fails fast on typos before touching reservation state.</item>
///   <item><see cref="SeatAvailableHandler"/> — read-only contention check ahead of the aggregate.</item>
///   <item><see cref="CustomerNotBlockedHandler"/> — last because it may hit an out-of-process store.</item>
/// </list>
/// </summary>
public sealed class BookingValidationPipeline : IBookingValidationPipeline
{
    private readonly IBookingValidationHandler _head;

    public BookingValidationPipeline(
        EventPublishedHandler eventPublishedHandler,
        SeatExistsHandler seatExistsHandler,
        SeatAvailableHandler seatAvailableHandler,
        CustomerNotBlockedHandler customerNotBlockedHandler)
    {
        eventPublishedHandler
            .SetNext(seatExistsHandler)
            .SetNext(seatAvailableHandler)
            .SetNext(customerNotBlockedHandler);

        _head = eventPublishedHandler;
    }

    public Task<Result> ValidateAsync(BookingValidationContext context, CancellationToken cancellationToken) =>
        _head.HandleAsync(context, cancellationToken);
}
