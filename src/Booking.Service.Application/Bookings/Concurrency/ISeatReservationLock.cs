using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Bookings.Concurrency;

/// <summary>
/// Cross-replica mutex around a single seat in a single event. Acquired by
/// <c>ReserveSeatCommandHandler</c> *before* the Event aggregate is loaded, so two reservers
/// racing for the same seat see the contention here rather than at the database layer.
/// </summary>
/// <remarks>
/// This is the first line of defense against overselling — the optimistic-concurrency token on
/// <c>EventSeatEntity.Version</c> is the second line. Both are required because the lock can be lost
/// (TTL expiry, Redis hiccup) and the DB token can race (two reservers both within their lock window
/// because we briefly had two Redis cluster heads).
/// </remarks>
public interface ISeatReservationLock
{
    /// <summary>
    /// Tries to acquire the lock for the given seat. Returns null when another replica currently holds it.
    /// </summary>
    Task<ISeatReservationLockHandle?> TryAcquireAsync(
        EventId eventId,
        SeatNumber seatNumber,
        CancellationToken cancellationToken);
}

/// <summary>
/// Lock handle. Disposing releases the lock; disposal that fails (e.g. Redis unreachable) is logged
/// but never throws so the caller's happy path is unaffected by lock-release fault.
/// </summary>
public interface ISeatReservationLockHandle : IAsyncDisposable
{
}
