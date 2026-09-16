namespace Booking.Service.Infrastructure.Configuration;

/// <summary>
/// Configuration-driven list of blocked customer ids. Replace with a persistent store when the
/// product requires it; the application contract <c>ICustomerBlocklist</c> stays the same.
/// </summary>
public sealed class BookingCustomerBlocklistOptions
{
    public const string SectionName = "BookingCustomerBlocklist";

    public List<Guid> BlockedCustomerIds { get; init; } = [];
}
