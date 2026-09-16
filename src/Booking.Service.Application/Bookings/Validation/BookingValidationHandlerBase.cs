using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Base class that wires up the next-pointer behavior so concrete handlers only declare their own check.
/// </summary>
public abstract class BookingValidationHandlerBase : IBookingValidationHandler
{
    private IBookingValidationHandler? _next;

    public IBookingValidationHandler SetNext(IBookingValidationHandler next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        return next;
    }

    public async Task<Result> HandleAsync(BookingValidationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await ValidateAsync(context, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        return _next is null
            ? Result.Success()
            : await _next.HandleAsync(context, cancellationToken);
    }

    /// <summary>
    /// Implemented by each concrete handler. Returns success to let the chain continue, or a failure
    /// result to short-circuit.
    /// </summary>
    protected abstract Task<Result> ValidateAsync(BookingValidationContext context, CancellationToken cancellationToken);
}
