namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class OutboxMessageEntity
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string RoutingKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime OccurredOnUtc { get; set; }

    public DateTime? ProcessedOnUtc { get; set; }
}