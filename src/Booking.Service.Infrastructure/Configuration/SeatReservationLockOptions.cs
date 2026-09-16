namespace Booking.Service.Infrastructure.Configuration;

/// <summary>
/// Configuration for the per-seat distributed lock used by <c>ReserveSeatCommandHandler</c>.
/// </summary>
public sealed class SeatReservationLockOptions
{
    public const string SectionName = "SeatReservationLock";

    /// <summary>Connection string for Redis. When empty, an in-process semaphore-per-key fallback is used.</summary>
    public string? RedisConnection { get; init; }

    /// <summary>Lease length. Must comfortably exceed the worst-case reserve-seat handler latency.</summary>
    public int LeaseSeconds { get; init; } = 3;

    /// <summary>Cap on how long the handler waits for the lock before giving up.</summary>
    public int WaitMilliseconds { get; init; } = 250;

    /// <summary>Backoff between RedLockNet retry attempts within the wait window.</summary>
    public int RetryMilliseconds { get; init; } = 25;

    /// <summary>Key prefix in the lock store. Useful when multiple tenants share a Redis instance.</summary>
    public string KeyPrefix { get; init; } = "eventhub:seat:";
}
