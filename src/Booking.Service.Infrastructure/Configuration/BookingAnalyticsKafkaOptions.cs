namespace Booking.Service.Infrastructure.Configuration;

public sealed class BookingAnalyticsKafkaOptions
{
    public string BootstrapServers { get; init; } = "localhost:9092";

    public string BookingStartedTopic { get; init; } = "analytics.booking-started";
}
