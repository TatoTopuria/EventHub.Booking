using Booking.Service.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Observability;

/// <summary>
/// BackgroundService that drives <see cref="BookingHealthEmitterService"/> on a fixed interval.
/// Pure plumbing — all DB work lives in the inner scoped service so unit tests can invoke
/// <see cref="BookingHealthEmitterService.EmitOnceAsync"/> directly without the host loop.
/// </summary>
public sealed class BookingHealthEmitterWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BookingHealthEmitterOptions _options;
    private readonly ILogger<BookingHealthEmitterWorker> _logger;

    public BookingHealthEmitterWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BookingHealthEmitterOptions> options,
        ILogger<BookingHealthEmitterWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("BookingHealthEmitterWorkerDisabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.EmitIntervalSeconds));
        _logger.LogInformation(
            "BookingHealthEmitterWorkerStarted intervalSeconds={IntervalSeconds} outboxStuckAfterSeconds={OutboxStuckAfterSeconds}",
            interval.TotalSeconds,
            _options.OutboxStuckAfterSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<BookingHealthEmitterService>();
                await service.EmitOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "BookingHealthEmitterTickFailed message={Message}",
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

        _logger.LogInformation("BookingHealthEmitterWorkerStopped");
    }
}
