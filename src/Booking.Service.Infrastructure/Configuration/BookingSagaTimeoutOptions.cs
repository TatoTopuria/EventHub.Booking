namespace Booking.Service.Infrastructure.Configuration;

/// <summary>
/// Configuration for <c>BookingSagaTimeoutWorker</c>, the background sweep that detects sagas
/// stuck in <c>Refunding</c> longer than <see cref="RefundTimeoutSeconds"/> and publishes a
/// synthetic <c>RefundTimeoutExpiredV1</c> so the state machine can finalize them. Closes audit
/// gap G6.
/// </summary>
public sealed class BookingSagaTimeoutOptions
{
    public const string SectionName = "BookingSagaTimeout";

    /// <summary>
    /// Disabled in tests that drive the saga end-to-end (so the worker does not race the assertion).
    /// Enabled in production.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Wall-clock budget for the <c>Refunding</c> wait. After this many seconds the worker
    /// publishes <c>RefundTimeoutExpiredV1</c> and the saga transitions to <c>RefundFailed</c>.
    /// </summary>
    /// <remarks>
    /// Default 60 s — pragmatic for the test-mode Stripe/PayPal providers, which respond inside
    /// 1–2 s under load. Production should size this against the slowest provider SLA + a margin.
    /// </remarks>
    public int RefundTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Interval between sweep passes. Shorter = lower escalation latency, higher DB chatter.
    /// Default 15 s — bounds the worst-case escalation latency at <c>RefundTimeoutSeconds + 15 s</c>.
    /// </summary>
    public int ScanIntervalSeconds { get; init; } = 15;

    /// <summary>
    /// Max number of stale sagas processed per sweep. Cap protects RabbitMQ from a publish storm
    /// if a Payment.Service outage strands thousands of sagas at once — they will get picked up
    /// across successive sweeps instead.
    /// </summary>
    public int BatchSize { get; init; } = 100;
}
