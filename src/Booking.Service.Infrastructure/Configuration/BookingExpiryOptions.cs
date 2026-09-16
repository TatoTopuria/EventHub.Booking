namespace Booking.Service.Infrastructure.Configuration;

/// <summary>
/// Configuration for the background job that expires unpaid bookings after the domain-defined window.
/// </summary>
public sealed class BookingExpiryOptions
{
    public const string SectionName = "BookingExpiry";

    /// <summary>Master kill-switch. Set false in tests that drive expiry manually.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Time between sweeps. Keep well below <see cref="ExpiryAfterMinutes"/> so the cleanup tail is bounded.</summary>
    public int ScanIntervalSeconds { get; init; } = 30;

    /// <summary>Must match the domain invariant in <c>Booking.ExpireIfUnpaid</c> (currently 10 minutes).</summary>
    public int ExpiryAfterMinutes { get; init; } = 10;

    /// <summary>Maximum number of bookings the worker expires per sweep. Caps DB pressure under burst load.</summary>
    public int BatchSize { get; init; } = 100;

    public BookingExpiryLockOptions Lock { get; init; } = new();
}

/// <summary>
/// Distributed-lock configuration so only one replica runs the expiry sweep at a time.
/// When <see cref="RedisConnection"/> is empty, the worker falls back to an in-process lock —
/// safe for single-instance dev, NOT for multi-replica production.
/// </summary>
public sealed class BookingExpiryLockOptions
{
    public string? RedisConnection { get; init; }

    public string Key { get; init; } = "eventhub:booking-expiry:lock";

    /// <summary>Lock lease length. Must comfortably exceed the longest expected sweep duration.</summary>
    public int TtlSeconds { get; init; } = 30;
}
