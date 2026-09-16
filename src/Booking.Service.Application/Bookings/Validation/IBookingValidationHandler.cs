using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// One node in the booking-validation chain (GoF Chain of Responsibility).
/// </summary>
/// <remarks>
/// Each handler runs one focused check and either short-circuits with a failure result or hands
/// off to the next link. Concrete handlers should derive from <see cref="BookingValidationHandlerBase"/>
/// rather than implement this interface directly, so the next-pointer plumbing stays in one place.
/// </remarks>
public interface IBookingValidationHandler
{
    /// <summary>
    /// Sets the next link in the chain. Returns the next handler so calls can be fluently composed.
    /// </summary>
    IBookingValidationHandler SetNext(IBookingValidationHandler next);

    /// <summary>
    /// Runs this handler and, on success, delegates to the next link if any.
    /// </summary>
    Task<Result> HandleAsync(BookingValidationContext context, CancellationToken cancellationToken);
}
