namespace Booking.Service.Infrastructure.Configuration;

/// <summary>
/// Configuration for the F7 observability emitters — the periodic background services that turn
/// transactional state (outbox backlog, saga state counts) into structured log lines so the
/// Kibana dashboards have data without giving Kibana direct DB access.
/// </summary>
/// <remarks>
/// Disabled in tests so the polling loops do not race deterministic assertions; enabled in dev
/// and production where the Kibana dashboards consume the resulting log indices.
/// </remarks>
public sealed class BookingHealthEmitterOptions
{
    public const string SectionName = "BookingHealthEmitter";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Interval between health snapshots. 30 s strikes the balance between dashboard freshness
    /// and DB-query noise — each tick is one cheap aggregate query per table.
    /// </summary>
    public int EmitIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Outbox rows older than this without <c>ProcessedOnUtc</c> qualify as "stuck". The emitter
    /// logs both total backlog and stuck-only counts so the Kibana lag panel can plot both.
    /// </summary>
    public int OutboxStuckAfterSeconds { get; init; } = 60;
}
