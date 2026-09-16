namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class InboxMessageEntity
{
    public Guid Id { get; set; }

    public string MessageId { get; set; } = string.Empty;

    public DateTime? ProcessedAtUtc { get; set; }

    public DateTime? ProcessingStartedAtUtc { get; set; }

    public string Status { get; set; } = InboxMessageStatus.Processing;
}

public static class InboxMessageStatus
{
    public const string Processing = "processing";
    public const string Processed = "processed";
}