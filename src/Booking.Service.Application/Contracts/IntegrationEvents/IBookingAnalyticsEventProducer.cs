namespace Booking.Service.Application.Contracts.IntegrationEvents;

public sealed record BookingStartedAnalyticsEvent(Guid EventId, Guid UserId, DateTime StartedAtUtc);

public interface IBookingAnalyticsEventProducer
{
    Task PublishBookingStartedAsync(BookingStartedAnalyticsEvent evt, string? correlationId, CancellationToken cancellationToken = default);
}
