using Booking.Service.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Saga;

/// <summary>
/// BackgroundService that drives <see cref="BookingSagaTimeoutService"/> on a fixed interval.
/// Pure plumbing — all detection logic lives in the inner scoped service so it can be unit-tested
/// without spinning up a host.
/// </summary>
/// <remarks>
/// Mirrors the established <c>BookingExpiryWorker</c> pattern: per-tick scope, defensive
/// catch-all to keep the loop alive across transient infra hiccups, configurable interval and
/// enabled flag. Multiple replicas of Booking.Service can run this worker safely without a
/// distributed lock — see <see cref="BookingSagaTimeoutService"/> for the idempotency argument.
/// </remarks>
public sealed class BookingSagaTimeoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BookingSagaTimeoutOptions _options;
    private readonly ILogger<BookingSagaTimeoutWorker> _logger;

    public BookingSagaTimeoutWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BookingSagaTimeoutOptions> options,
        ILogger<BookingSagaTimeoutWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("BookingSagaTimeoutWorkerDisabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.ScanIntervalSeconds));
        _logger.LogInformation(
            "BookingSagaTimeoutWorkerStarted intervalSeconds={IntervalSeconds} refundTimeoutSeconds={RefundTimeoutSeconds} batchSize={BatchSize}",
            interval.TotalSeconds,
            _options.RefundTimeoutSeconds,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<BookingSagaTimeoutService>();
                var published = await service.RunOnceAsync(stoppingToken);
                if (published > 0)
                {
                    _logger.LogInformation(
                        "BookingSagaTimeoutWorkerSweepCompleted publishedCount={Published}",
                        published);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Sweep failure — log and keep the loop alive. Sleeping is what cools the cycle.
                _logger.LogError(
                    exception,
                    "BookingSagaTimeoutWorkerSweepFailed message={Message}",
                    exception.Message);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("BookingSagaTimeoutWorkerStopped");
    }
}
