using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Rejects reservations for seats that are already reserved on the event aggregate.
/// </summary>
/// <remarks>
/// This is a fast pre-check; the Event aggregate enforces the same invariant atomically inside
/// <c>MarkSeatReserved</c>. Doing it here first means we can return a useful error message before
/// any state mutation occurs.
/// </remarks>
public sealed class SeatAvailableHandler : BookingValidationHandlerBase
{
    protected override Task<Result> ValidateAsync(BookingValidationContext context, CancellationToken cancellationToken)
    {
        if (context.EventAggregate.ReservedSeats.Contains(context.SeatNumber))
        {
            return Task.FromResult(Result.Failure(
                $"Seat '{context.SeatNumber.Value}' is already reserved for event {context.EventAggregate.Id.Value:D}."));
        }

        return Task.FromResult(Result.Success());
    }
}
