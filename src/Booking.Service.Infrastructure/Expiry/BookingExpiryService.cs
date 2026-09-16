using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Infrastructure.Configuration;
using BuildingBlocks.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Expiry;

/// <summary>
/// Single-shot expiry sweep: acquires the distributed lock, expires due bookings, releases the lock.
/// Lives outside the hosted-service loop so unit and integration tests can drive it directly.
/// </summary>
public sealed class BookingExpiryService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IBookingUnitOfWork _unitOfWork;
    private readonly IBookingExpiryLock _expiryLock;
    private readonly IClockProvider _clock;
    private readonly BookingExpiryOptions _options;
    private readonly ILogger<BookingExpiryService> _logger;

    public BookingExpiryService(
        IBookingRepository bookingRepository,
        IEventRepository eventRepository,
        IBookingUnitOfWork unitOfWork,
        IBookingExpiryLock expiryLock,
        IClockProvider clock,
        IOptions<BookingExpiryOptions> options,
        ILogger<BookingExpiryService> logger)
    {
        _bookingRepository = bookingRepository;
        _eventRepository = eventRepository;
        _unitOfWork = unitOfWork;
        _expiryLock = expiryLock;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the number of bookings successfully expired. Returns 0 (and logs a debug line)
    /// when another replica holds the lock.
    /// </summary>
    public async Task<BookingExpirySweepResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var lockHandle = await _expiryLock.TryAcquireAsync(cancellationToken);
        if (lockHandle is null)
        {
            _logger.LogDebug("BookingExpirySkipped reason={Reason}", "LockHeldByAnotherReplica");
            return BookingExpirySweepResult.LockMissed;
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var cutoffUtc = nowUtc.AddMinutes(-Math.Max(1, _options.ExpiryAfterMinutes));

        var candidates = await _bookingRepository.GetExpiringPendingAsync(
            cutoffUtc,
            _options.BatchSize,
            cancellationToken);

        if (candidates.Count == 0)
        {
            return new BookingExpirySweepResult(Expired: 0, Failed: 0, Skipped: false);
        }

        var expired = 0;
        var failed = 0;

        foreach (var booking in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var expireResult = booking.ExpireIfUnpaid(nowUtc);
                if (expireResult.IsFailure)
                {
                    // Aggregate refused (e.g. booking became paid/confirmed between query and load).
                    // Not an error condition — just skip and continue.
                    _logger.LogDebug(
                        "BookingExpirySkippedSingle bookingId={BookingId} reason={Reason}",
                        booking.Id,
                        expireResult.Error);
                    continue;
                }

                var eventAggregate = await _eventRepository.GetByIdAsync(booking.EventId, cancellationToken);
                if (eventAggregate is null)
                {
                    _logger.LogWarning(
                        "BookingExpiryEventMissing bookingId={BookingId} eventId={EventId}",
                        booking.Id,
                        booking.EventId.Value);
                }
                else
                {
                    foreach (var reservedSeat in booking.ReservedSeats)
                    {
                        var releaseResult = eventAggregate.MarkSeatReleased(reservedSeat);
                        if (releaseResult.IsFailure)
                        {
                            _logger.LogWarning(
                                "BookingExpirySeatReleaseFailed bookingId={BookingId} seat={Seat} reason={Reason}",
                                booking.Id,
                                reservedSeat.Value,
                                releaseResult.Error);
                        }
                    }

                    await _eventRepository.UpdateAsync(eventAggregate, cancellationToken);
                }

                await _bookingRepository.UpdateAsync(booking, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                expired++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                _logger.LogError(
                    exception,
                    "BookingExpiryFailedSingle bookingId={BookingId} message={Message}",
                    booking.Id,
                    exception.Message);
            }
        }

        _logger.LogInformation(
            "BookingExpirySweepCompleted expired={Expired} failed={Failed} batch={Batch}",
            expired,
            failed,
            candidates.Count);

        return new BookingExpirySweepResult(expired, failed, Skipped: false);
    }
}

/// <summary>Outcome of a single expiry sweep — surfaced for tests and for the worker log line.</summary>
public sealed record BookingExpirySweepResult(int Expired, int Failed, bool Skipped)
{
    /// <summary>Sentinel result for sweeps that were skipped because the distributed lock was held elsewhere.</summary>
    public static BookingExpirySweepResult LockMissed { get; } = new(0, 0, true);
}
