using Booking.Service.Application.Bookings.Validation;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Validation;

/// <summary>
/// Reads the blocked-customer set from configuration once per options snapshot. Suitable for the
/// learning-project scope; production deployments should swap in a persistent-store implementation.
/// </summary>
public sealed class InMemoryCustomerBlocklist : ICustomerBlocklist
{
    private readonly HashSet<Guid> _blockedIds;

    public InMemoryCustomerBlocklist(IOptions<BookingCustomerBlocklistOptions> options)
    {
        _blockedIds = options.Value.BlockedCustomerIds.ToHashSet();
    }

    public Task<bool> IsBlockedAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_blockedIds.Contains(customerId.Value));
    }
}
