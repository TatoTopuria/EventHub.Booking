using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Persistence;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Abstractions.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Saga;

/// <summary>
/// Scoped per sweep. Finds sagas sitting in <c>Refunding</c> beyond the configured timeout and
/// publishes <see cref="RefundTimeoutExpiredV1"/> so the state machine can transition them to
/// <c>RefundFailed</c> and finalize. The state machine ultimately owns the transition — this
/// service is just the detector.
/// </summary>
/// <remarks>
/// Idempotent across replicas without a distributed lock: the MassTransit saga repository
/// (configured with <c>PostgresLockStatementProvider</c>) row-locks each saga instance, so two
/// replicas publishing the same <c>RefundTimeoutExpiredV1</c> will serialise at the repository
/// boundary — the first wins and finalises the saga, the second arrives at a finalised instance
/// and is dropped by the dispatcher.
///
/// Polling instead of event-driven is the deliberate choice — see DECISIONS.md F5 entry.
/// </remarks>
public sealed class BookingSagaTimeoutService
{
    private const string RefundingStateName = nameof(BookingPaymentSagaStateMachine.Refunding);

    private readonly BookingDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly BookingSagaTimeoutOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly ILogger<BookingSagaTimeoutService> _logger;

    public BookingSagaTimeoutService(
        BookingDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IOptions<BookingSagaTimeoutOptions> options,
        TimeProvider timeProvider,
        ICorrelationContextAccessor correlationContextAccessor,
        ILogger<BookingSagaTimeoutService> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _options = options.Value;
        _timeProvider = timeProvider;
        _correlationContextAccessor = correlationContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Runs one sweep. Returns the number of timeout messages published. Test-callable directly so
    /// the worker loop does not need to be exercised in unit tests.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now.AddSeconds(-Math.Max(1, _options.RefundTimeoutSeconds));
        var batchSize = Math.Clamp(_options.BatchSize, 1, 1000);

        // AsNoTracking — we do not mutate the saga state directly. The state machine owns the
        // mutation; we only publish the synthetic event and let MassTransit drive the transition.
        var staleSagas = await _dbContext.BookingPaymentSagaStates
            .AsNoTracking()
            .Where(saga =>
                saga.CurrentState == RefundingStateName
                && saga.RefundRequestedAtUtc != null
                && saga.RefundRequestedAtUtc < cutoff)
            .OrderBy(saga => saga.RefundRequestedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (staleSagas.Count == 0)
        {
            return 0;
        }

        var published = 0;
        foreach (var saga in staleSagas)
        {
            var elapsedSeconds = (int)Math.Round((now - saga.RefundRequestedAtUtc!.Value).TotalSeconds);
            var correlationId = ResolveCorrelationId(saga.BookingId);

            // Single structured log line per escalation. Kibana dashboards (F7) alert on this
            // event-name pattern; do not rename without updating the saved object.
            _logger.LogWarning(
                "SagaRefundEscalated bookingId={BookingId} paymentIntentId={PaymentIntentId} elapsedSeconds={ElapsedSeconds} cutoffUtc={CutoffUtc}",
                saga.BookingId,
                saga.PaymentIntentId,
                elapsedSeconds,
                cutoff);

            try
            {
                await _publishEndpoint.Publish(
                    new RefundTimeoutExpiredV1(
                        MessageId: Guid.NewGuid(),
                        CorrelationId: correlationId,
                        BookingId: saga.BookingId,
                        PaymentIntentId: saga.PaymentIntentId ?? Guid.Empty,
                        ElapsedSeconds: elapsedSeconds,
                        OccurredOnUtc: now),
                    publishContext =>
                    {
                        publishContext.Headers.Set("X-Correlation-ID", correlationId);
                        publishContext.Headers.Set("correlation-id", correlationId);
                    },
                    cancellationToken);

                published++;
            }
            catch (Exception exception)
            {
                // Don't bail the whole sweep on a single publish failure — log and continue with
                // the next saga. The failed one will be re-detected on the next sweep.
                _logger.LogError(
                    exception,
                    "SagaRefundEscalationPublishFailed bookingId={BookingId} message={Message}",
                    saga.BookingId,
                    exception.Message);
            }
        }

        return published;
    }

    private string ResolveCorrelationId(Guid bookingId)
    {
        // Per-saga correlation id keeps every log line + outbound header tied back to the original
        // booking. If the current request scope already carries a correlation id (e.g. invoked
        // from an admin endpoint), inherit it; otherwise mint a fresh one keyed by booking.
        var existing = _correlationContextAccessor.CorrelationId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return $"saga-timeout:{bookingId:N}";
    }
}
