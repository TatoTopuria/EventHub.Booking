using System.Text;
using Booking.Service.Application.Contracts.IntegrationEvents;
using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Persistence;
using Booking.Service.Infrastructure.Persistence.Models;
using BuildingBlocks.Abstractions.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text.Json;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class BookingOutboxPublisher(
    IServiceScopeFactory serviceScopeFactory,
    IBookingAnalyticsEventProducer analyticsEventProducer,
    IOptions<BookingMessagingOptions> options,
    ILogger<BookingOutboxPublisher> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan AnalyticsPublishTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private BookingMessagingOptions _options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PublisherEnabled)
        {
            logger.LogInformation("BookingOutboxPublisherDisabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingMessagesAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "BookingOutboxPublisherRetry delaySeconds={DelaySeconds}", 5);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PublishIntervalSeconds), stoppingToken);
        }
    }

    private async Task PublishPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var businessMessages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null && message.Type != nameof(BookingStartedAnalyticsEvent))
            .OrderBy(message => message.OccurredOnUtc)
            .Take(_options.PublishBatchSize)
            .ToListAsync(cancellationToken);

        var remainingCapacity = _options.PublishBatchSize - businessMessages.Count;
        var analyticsMessages = remainingCapacity > 0
            ? await dbContext.OutboxMessages
                .Where(message => message.ProcessedOnUtc == null && message.Type == nameof(BookingStartedAnalyticsEvent))
                .OrderBy(message => message.OccurredOnUtc)
                .Take(remainingCapacity)
                .ToListAsync(cancellationToken)
            : [];

        var messages = businessMessages.Concat(analyticsMessages).ToList();

        if (messages.Count == 0)
        {
            return;
        }

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        foreach (var message in messages)
        {
            try
            {
                var properties = new BasicProperties
                {
                    Persistent = true,
                    MessageId = message.Id.ToString("D"),
                    CorrelationId = message.CorrelationId,
                    Type = message.Type,
                    Headers = new Dictionary<string, object?>
                    {
                        ["X-Correlation-ID"] = message.CorrelationId,
                        ["X-Message-ID"] = message.Id.ToString("D")
                    }
                };

                if (await TryPublishAnalyticsAsync(message, analyticsEventProducer, cancellationToken))
                {
                    message.ProcessedOnUtc = timeProvider.GetUtcNow().UtcDateTime;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                if (await TryPublishWithMassTransitAsync(message, publishEndpoint, cancellationToken))
                {
                    message.ProcessedOnUtc = timeProvider.GetUtcNow().UtcDateTime;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                await channel.BasicPublishAsync(
                    exchange: _options.Exchange,
                    routingKey: ResolveRoutingKey(message.RoutingKey),
                    mandatory: false,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload),
                    cancellationToken: cancellationToken);

                message.ProcessedOnUtc = timeProvider.GetUtcNow().UtcDateTime;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "BookingOutboxPublishDeferred type={Type} messageId={MessageId} reason={Reason}",
                    message.Type,
                    message.Id,
                    "Timeout");
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "BookingOutboxPublishDeferred type={Type} messageId={MessageId}",
                    message.Type,
                    message.Id);
            }
        }
    }

    private static async Task<bool> TryPublishAnalyticsAsync(
        OutboxMessageEntity message,
        IBookingAnalyticsEventProducer analyticsEventProducer,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(message.Type, nameof(BookingStartedAnalyticsEvent), StringComparison.Ordinal))
        {
            return false;
        }

        var analyticsEvent = JsonSerializer.Deserialize<BookingStartedAnalyticsEvent>(message.Payload, SerializerOptions);
        if (analyticsEvent is null)
        {
            throw new InvalidOperationException("Outbox payload for BookingStartedAnalyticsEvent could not be deserialized.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AnalyticsPublishTimeout);

        await analyticsEventProducer.PublishBookingStartedAsync(analyticsEvent, message.CorrelationId, timeoutCts.Token);
        return true;
    }

    private string ResolveRoutingKey(string routingKey)
    {
        return routingKey switch
        {
            "booking.seat.reserved" => _options.SeatReservedRoutingKey,
            "booking.seat.released" => _options.SeatReleasedRoutingKey,
            "booking.event.updated" => _options.EventUpdatedRoutingKey,
            _ => routingKey
        };
    }

    private static Task<bool> TryPublishWithMassTransitAsync(OutboxMessageEntity message, IPublishEndpoint publishEndpoint, CancellationToken cancellationToken)
    {
        return message.Type switch
        {
            nameof(ReserveSeatStartedV1) => PublishTypedAsync<ReserveSeatStartedV1>(message, publishEndpoint, cancellationToken),
            nameof(BookingConfirmedV1) => PublishTypedAsync<BookingConfirmedV1>(message, publishEndpoint, cancellationToken),
            _ => Task.FromResult(false)
        };
    }

    private static async Task<bool> PublishTypedAsync<TMessage>(OutboxMessageEntity message, IPublishEndpoint publishEndpoint, CancellationToken cancellationToken)
        where TMessage : class
    {
        var typedMessage = JsonSerializer.Deserialize<TMessage>(message.Payload, SerializerOptions);
        if (typedMessage is null)
        {
            throw new InvalidOperationException($"Outbox payload for '{message.Type}' could not be deserialized.");
        }

        await publishEndpoint.Publish(
            typedMessage,
            publishContext =>
            {
                publishContext.Headers.Set("X-Correlation-ID", message.CorrelationId);
                publishContext.Headers.Set("correlation-id", message.CorrelationId);
            },
            cancellationToken);
        return true;
    }
}