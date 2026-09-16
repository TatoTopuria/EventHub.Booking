using Booking.Service.Application.Bookings.Concurrency;
using Booking.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedLockNet;
using EventId = Booking.Service.Domain.ValueObjects.EventId;
using SeatNumber = Booking.Service.Domain.ValueObjects.SeatNumber;

namespace Booking.Service.Infrastructure.Concurrency;

/// <summary>
/// RedLock-based <see cref="ISeatReservationLock"/>. Uses the configured
/// <see cref="IDistributedLockFactory"/> from <see cref="RedLockNet.SERedis"/>, which implements the
/// canonical Redlock algorithm (with a single Redis node in our case, which collapses to
/// <c>SET key value NX PX ttl</c> + CAS-delete release).
/// </summary>
public sealed class RedLockSeatReservationLock : ISeatReservationLock
{
    private readonly IDistributedLockFactory _lockFactory;
    private readonly SeatReservationLockOptions _options;
    private readonly ILogger<RedLockSeatReservationLock> _logger;

    public RedLockSeatReservationLock(
        IDistributedLockFactory lockFactory,
        IOptions<SeatReservationLockOptions> options,
        ILogger<RedLockSeatReservationLock> logger)
    {
        _lockFactory = lockFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ISeatReservationLockHandle?> TryAcquireAsync(
        EventId eventId,
        SeatNumber seatNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = $"{_options.KeyPrefix}{eventId.Value:D}:{seatNumber.Value}";
        var expiry = TimeSpan.FromSeconds(Math.Max(1, _options.LeaseSeconds));
        var wait = TimeSpan.FromMilliseconds(Math.Max(0, _options.WaitMilliseconds));
        var retry = TimeSpan.FromMilliseconds(Math.Max(10, _options.RetryMilliseconds));

        IRedLock? redLock = null;
        try
        {
            redLock = await _lockFactory.CreateLockAsync(key, expiry, wait, retry, cancellationToken);
        }
        catch (Exception exception)
        {
            // Redis unreachable / configuration issue. Treat as "could not acquire" so the caller
            // returns a clean 409 instead of leaking the transport error.
            _logger.LogWarning(
                exception,
                "SeatReservationLockAcquireFailed key={Key} message={Message}",
                key,
                exception.Message);
            return null;
        }

        if (!redLock.IsAcquired)
        {
            await redLock.DisposeAsync();
            return null;
        }

        return new RedLockHandle(redLock, key, _logger);
    }

    private sealed class RedLockHandle(IRedLock redLock, string key, ILogger<RedLockSeatReservationLock> logger)
        : ISeatReservationLockHandle
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await redLock.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "SeatReservationLockReleaseFailed key={Key} message={Message}",
                    key,
                    exception.Message);
            }
        }
    }
}
