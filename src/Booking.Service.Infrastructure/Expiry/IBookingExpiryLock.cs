namespace Booking.Service.Infrastructure.Expiry;

/// <summary>
/// Distributed mutex around the booking-expiry sweep. Only the replica that successfully acquires
/// the lock runs the sweep for the current tick; others skip silently and try again next tick.
/// </summary>
public interface IBookingExpiryLock
{
    /// <summary>
    /// Tries to acquire the lock with the configured TTL. Returns null when the lock is held by
    /// another replica; the caller must NOT run the sweep in that case.
    /// </summary>
    Task<IBookingExpiryLockHandle?> TryAcquireAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Lock handle. Disposing releases the lock — disposal that fails (e.g. Redis unreachable) is logged
/// but never throws so the worker loop survives intermittent infra hiccups.
/// </summary>
public interface IBookingExpiryLockHandle : IAsyncDisposable
{
}
