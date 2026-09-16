using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Observability;

/// <summary>
/// Scoped per tick — reads cheap aggregate counts off the saga state and outbox tables and
/// emits two structured log lines: <c>EventHubSagaProgress</c> and <c>EventHubOutboxLag</c>.
/// These become the data source for the F7 Kibana dashboards (saga progress by state, outbox
/// backlog over time) without giving the dashboard layer direct DB access.
/// </summary>
/// <remarks>
/// Every query is an aggregate (COUNT, MAX) with an index-supported predicate, so the per-tick
/// cost is negligible even as the tables grow. Failures are caught and logged so a transient
/// DB hiccup does not kill the worker loop.
/// </remarks>
public sealed class BookingHealthEmitterService
{
    private readonly BookingDbContext _dbContext;
    private readonly BookingHealthEmitterOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingHealthEmitterService> _logger;

    public BookingHealthEmitterService(
        BookingDbContext dbContext,
        IOptions<BookingHealthEmitterOptions> options,
        TimeProvider timeProvider,
        ILogger<BookingHealthEmitterService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task EmitOnceAsync(CancellationToken cancellationToken)
    {
        await EmitSagaProgressAsync(cancellationToken);
        await EmitOutboxLagAsync(cancellationToken);
    }

    /// <summary>
    /// One row per non-finalized saga, grouped by <c>CurrentState</c>. Emitted as a separate log
    /// line per state so the Kibana panel can filter by <c>state</c> to build a stacked-area
    /// "where are sagas sitting?" visualisation.
    /// </summary>
    private async Task EmitSagaProgressAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _dbContext.BookingPaymentSagaStates
                .AsNoTracking()
                .GroupBy(saga => saga.CurrentState)
                .Select(g => new { state = g.Key, count = g.LongCount() })
                .ToListAsync(cancellationToken);

            if (snapshot.Count == 0)
            {
                // Emit a zero-count baseline anyway so a Kibana panel that filters by
                // EventHubSagaProgress always has data points; absent data is harder to debug than
                // explicit zero.
                _logger.LogInformation(
                    "EventHubSagaProgress state={State} count={Count}",
                    "(empty)",
                    0L);
                return;
            }

            foreach (var entry in snapshot)
            {
                _logger.LogInformation(
                    "EventHubSagaProgress state={State} count={Count}",
                    entry.state,
                    entry.count);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "EventHubSagaProgressFailed message={Message}",
                exception.Message);
        }
    }

    /// <summary>
    /// Outbox backlog: total pending rows, "stuck" pending rows (older than
    /// <c>OutboxStuckAfterSeconds</c>), and age of the oldest pending row in seconds. Stuck-only
    /// is the actionable alert signal; total is the trend metric.
    /// </summary>
    private async Task EmitOutboxLagAsync(CancellationToken cancellationToken)
    {
        try
        {
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var stuckCutoff = nowUtc.AddSeconds(-Math.Max(1, _options.OutboxStuckAfterSeconds));

            var totals = await _dbContext.OutboxMessages
                .AsNoTracking()
                .Where(message => message.ProcessedOnUtc == null)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    pendingTotal = g.LongCount(),
                    stuckTotal = g.LongCount(message => message.OccurredOnUtc < stuckCutoff),
                    oldestPendingUtc = (DateTime?)g.Min(message => message.OccurredOnUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var pendingTotal = totals?.pendingTotal ?? 0L;
            var stuckTotal = totals?.stuckTotal ?? 0L;
            var oldestPendingAgeSeconds = totals?.oldestPendingUtc is { } oldestUtc
                ? (long)Math.Round((nowUtc - oldestUtc).TotalSeconds)
                : 0L;

            _logger.LogInformation(
                "EventHubOutboxLag pendingTotal={PendingTotal} stuckTotal={StuckTotal} stuckAfterSeconds={StuckAfterSeconds} oldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                pendingTotal,
                stuckTotal,
                _options.OutboxStuckAfterSeconds,
                oldestPendingAgeSeconds);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "EventHubOutboxLagFailed message={Message}",
                exception.Message);
        }
    }
}
