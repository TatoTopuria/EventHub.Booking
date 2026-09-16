using System.Collections.Concurrent;
using Booking.Service.Application.Bookings.Concurrency;
using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Infrastructure.Concurrency;

/// <summary>
/// Single-process implementation of <see cref="ISeatReservationLock"/>. Uses a semaphore per
/// (eventId, seatNumber) key so reservers for different seats never block each other.
/// </summary>
/// <remarks>
/// Safe only for a single Booking.Service replica. Production multi-replica deployments must
/// configure the Redis variant via <c>SeatReservationLock:RedisConnection</c>.
/// </remarks>
public sealed class InProcessSeatReservationLock : ISeatReservationLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public Task<ISeatReservationLockHandle?> TryAcquireAsync(
        EventId eventId,
        SeatNumber seatNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = BuildKey(eventId, seatNumber);
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));

        if (!gate.Wait(TimeSpan.Zero))
        {
            return Task.FromResult<ISeatReservationLockHandle?>(null);
        }

        return Task.FromResult<ISeatReservationLockHandle?>(new InProcessHandle(gate));
    }

    private static string BuildKey(EventId eventId, SeatNumber seatNumber) =>
        $"{eventId.Value:D}:{seatNumber.Value}";

    private sealed class InProcessHandle(SemaphoreSlim gate) : ISeatReservationLockHandle
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
