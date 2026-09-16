using Booking.Service.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Expiry;

/// <summary>
/// Hosted service that drives <see cref="BookingExpiryService"/> on a fixed interval.
/// Pure plumbing — all expiry logic lives in the inner service so it can be unit tested without
/// spinning up a host.
/// </summary>
public sealed class BookingExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BookingExpiryOptions _options;
    private readonly ILogger<BookingExpiryWorker> _logger;

    public BookingExpiryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BookingExpiryOptions> options,
        ILogger<BookingExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("BookingExpiryWorkerDisabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.ScanIntervalSeconds));
        _logger.LogInformation(
            "BookingExpiryWorkerStarted intervalSeconds={IntervalSeconds} expiryAfterMinutes={ExpiryAfterMinutes}",
            interval.TotalSeconds,
            _options.ExpiryAfterMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<BookingExpiryService>();
                await service.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Never let the worker die from a transient failure. Sleep and try again.
                _logger.LogError(
                    exception,
                    "BookingExpiryWorkerSweepFailed message={Message}",
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

        _logger.LogInformation("BookingExpiryWorkerStopped");
    }
}
