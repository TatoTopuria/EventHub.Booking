using Booking.Service.Application.Contracts.IntegrationEvents;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class NoOpBookingAnalyticsEventProducer : IBookingAnalyticsEventProducer
{
    public Task PublishBookingStartedAsync(BookingStartedAnalyticsEvent evt, string? correlationId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
