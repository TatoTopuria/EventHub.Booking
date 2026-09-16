namespace Booking.Service.Infrastructure.Expiry;

/// <summary>
/// Single-process implementation of <see cref="IBookingExpiryLock"/>. Sufficient for a single-instance
/// dev deployment; NOT safe across multiple replicas. Production should configure the Redis variant.
/// </summary>
public sealed class InProcessBookingExpiryLock : IBookingExpiryLock
{
    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);

    public Task<IBookingExpiryLockHandle?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var acquired = _semaphore.Wait(TimeSpan.Zero);
        if (!acquired)
        {
            return Task.FromResult<IBookingExpiryLockHandle?>(null);
        }

        return Task.FromResult<IBookingExpiryLockHandle?>(new InProcessHandle(_semaphore));
    }

    private sealed class InProcessHandle(SemaphoreSlim semaphore) : IBookingExpiryLockHandle
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
