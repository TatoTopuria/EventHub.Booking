using System.Text;
using System.Text.Json;
using Booking.Service.Application.Contracts.IntegrationEvents;
using Booking.Service.Infrastructure.Configuration;
using BuildingBlocks.Abstractions.Observability;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class KafkaBookingAnalyticsEventProducer : IBookingAnalyticsEventProducer, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IProducer<string, string> _producer;
    private readonly BookingAnalyticsKafkaOptions _options;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    public KafkaBookingAnalyticsEventProducer(
        IOptions<BookingAnalyticsKafkaOptions> options,
        ICorrelationContextAccessor correlationContextAccessor)
    {
        _options = options.Value;
        _correlationContextAccessor = correlationContextAccessor;

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 200
        };

        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }

    public async Task PublishBookingStartedAsync(BookingStartedAnalyticsEvent evt, string? correlationId, CancellationToken cancellationToken = default)
    {
        var resolvedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? _correlationContextAccessor.CorrelationId
            : correlationId;

        var messageId = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(new
        {
            evt.EventId,
            UserId = evt.UserId.ToString("D"),
            StartedAt = evt.StartedAtUtc
        }, SerializerOptions);

        var message = new Message<string, string>
        {
            Key = evt.EventId.ToString("N"),
            Value = payload,
            Headers = BuildHeaders(messageId, resolvedCorrelationId, evt.UserId)
        };

        await _producer.ProduceAsync(_options.BookingStartedTopic, message, cancellationToken);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(3));
        _producer.Dispose();
    }

    private static Headers BuildHeaders(string messageId, string? correlationId, Guid userId)
    {
        var headers = new Headers
        {
            { "message-id", Encoding.UTF8.GetBytes(messageId) },
            { "user-id", Encoding.UTF8.GetBytes(userId.ToString("D")) }
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers.Add("correlation-id", Encoding.UTF8.GetBytes(correlationId));
        }

        return headers;
    }
}
