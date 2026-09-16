namespace Booking.Service.Infrastructure.Configuration;

public sealed class BookingMessagingOptions
{
    public const string SectionName = "BookingMessaging";

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string Exchange { get; init; } = "eventhub.booking";

    public string SeatReservedRoutingKey { get; init; } = "booking.seat.reserved";

    public string SeatReleasedRoutingKey { get; init; } = "booking.seat.released";

    /// <summary>
    /// Routing key for <c>EventUpdatedIntegrationEventV1</c> emitted by
    /// <see cref="Messaging.BookingOutboxPublisher"/>. Catalog (and any future projection service)
    /// binds its queue to this key on the shared <see cref="Exchange"/>.
    /// </summary>
    public string EventUpdatedRoutingKey { get; init; } = "booking.event.updated.v1";

    public int PublishBatchSize { get; init; } = 50;

    public int PublishIntervalSeconds { get; init; } = 1;

    public bool PublisherEnabled { get; init; } = true;
}