using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Single entrypoint for the booking-validation chain. Application code depends on this rather than
/// the concrete handler graph so the chain composition can change without ripple effects.
/// </summary>
public interface IBookingValidationPipeline
{
    Task<Result> ValidateAsync(BookingValidationContext context, CancellationToken cancellationToken);
}
