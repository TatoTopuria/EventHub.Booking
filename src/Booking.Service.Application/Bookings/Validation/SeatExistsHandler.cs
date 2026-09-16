using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Rejects reservations for seat numbers the event was never configured with.
/// </summary>
public sealed class SeatExistsHandler : BookingValidationHandlerBase
{
    protected override Task<Result> ValidateAsync(BookingValidationContext context, CancellationToken cancellationToken)
    {
        if (!context.EventAggregate.Seats.Contains(context.SeatNumber))
        {
            return Task.FromResult(Result.Failure(
                $"Seat '{context.SeatNumber.Value}' does not exist for event {context.EventAggregate.Id.Value:D}."));
        }

        return Task.FromResult(Result.Success());
    }
}
