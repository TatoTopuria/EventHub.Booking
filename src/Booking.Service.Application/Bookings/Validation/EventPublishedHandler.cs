using Booking.Service.Domain.Enums;
using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Rejects reservations against events that are still in <see cref="EventStatus.Draft"/> or have been
/// <see cref="EventStatus.Cancelled"/>. Only published events accept bookings.
/// </summary>
public sealed class EventPublishedHandler : BookingValidationHandlerBase
{
    protected override Task<Result> ValidateAsync(BookingValidationContext context, CancellationToken cancellationToken)
    {
        if (context.EventAggregate.Status != EventStatus.Published)
        {
            return Task.FromResult(Result.Failure(
                $"Event {context.EventAggregate.Id.Value:D} is not accepting bookings (status: {context.EventAggregate.Status})."));
        }

        return Task.FromResult(Result.Success());
    }
}
