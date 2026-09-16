using BuildingBlocks.Primitives;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Rejects reservations from customers that have been administratively blocked.
/// </summary>
public sealed class CustomerNotBlockedHandler : BookingValidationHandlerBase
{
    private readonly ICustomerBlocklist _blocklist;

    public CustomerNotBlockedHandler(ICustomerBlocklist blocklist)
    {
        _blocklist = blocklist;
    }

    protected override async Task<Result> ValidateAsync(BookingValidationContext context, CancellationToken cancellationToken)
    {
        var blocked = await _blocklist.IsBlockedAsync(context.CustomerId, cancellationToken);
        if (blocked)
        {
            return Result.Failure($"Customer {context.CustomerId.Value:D} is currently blocked from making bookings.");
        }

        return Result.Success();
    }
}
