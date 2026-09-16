using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Bookings.Validation;

/// <summary>
/// Queries whether a customer is currently barred from creating new bookings.
/// </summary>
/// <remarks>
/// Kept as an Application-level abstraction so the validation chain stays free of infrastructure
/// concerns. Today's implementation reads a static config list; a future implementation can query
/// a persistent block table without changing the chain.
/// </remarks>
public interface ICustomerBlocklist
{
    Task<bool> IsBlockedAsync(CustomerId customerId, CancellationToken cancellationToken);
}
