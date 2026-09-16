using System.Text.Json;
using Booking.Service.Application.Contracts.IntegrationEvents;
using Booking.Service.Domain.DomainEvents;
using Booking.Service.Infrastructure.Persistence.Models;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Domain;

namespace Booking.Service.Infrastructure.Messaging;

internal static class BookingOutboxMessageFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyCollection<OutboxMessageEntity> Create(
        IReadOnlyCollection<DomainEvent> domainEvents,
        string? correlationId)
    {
        if (domainEvents.Count == 0)
        {
            return [];
        }

        var resolvedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId;

        var messages = new List<OutboxMessageEntity>();
        foreach (var domainEvent in domainEvents)
        {
            switch (domainEvent)
            {
                case SeatReserved seatReserved:
                    messages.Add(CreateSeatReservedMessage(seatReserved, resolvedCorrelationId));
                    messages.Add(CreateReserveSeatStartedMessage(seatReserved, resolvedCorrelationId));
                    messages.Add(CreateBookingStartedAnalyticsMessage(seatReserved, resolvedCorrelationId));
                    break;
                case SeatReleased seatReleased:
                    messages.Add(CreateSeatReleasedMessage(seatReleased, resolvedCorrelationId));
                    break;
                case BookingConfirmed bookingConfirmed:
                    messages.Add(CreateBookingConfirmedMessage(bookingConfirmed, resolvedCorrelationId));
                    break;
                case EventUpdated eventUpdated:
                    messages.Add(CreateEventUpdatedMessage(eventUpdated, resolvedCorrelationId));
                    break;
            }
        }

        return messages;
    }

    private static OutboxMessageEntity CreateSeatReservedMessage(SeatReserved domainEvent, string correlationId)
    {
        var messageId = Guid.NewGuid();
        var integrationEvent = new SeatReservedIntegrationEvent(
            messageId,
            correlationId,
            domainEvent.EventId.Value,
            domainEvent.SeatNumber.Value,
            domainEvent.OccurredOnUtc);

        return new OutboxMessageEntity
        {
            Id = messageId,
            Type = nameof(SeatReservedIntegrationEvent),
            RoutingKey = "booking.seat.reserved",
            CorrelationId = correlationId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions)
        };
    }

    private static OutboxMessageEntity CreateSeatReleasedMessage(SeatReleased domainEvent, string correlationId)
    {
        var messageId = Guid.NewGuid();
        var integrationEvent = new SeatReleasedIntegrationEvent(
            messageId,
            correlationId,
            domainEvent.EventId.Value,
            domainEvent.SeatNumber.Value,
            domainEvent.OccurredOnUtc);

        return new OutboxMessageEntity
        {
            Id = messageId,
            Type = nameof(SeatReleasedIntegrationEvent),
            RoutingKey = "booking.seat.released",
            CorrelationId = correlationId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions)
        };
    }

    private static OutboxMessageEntity CreateReserveSeatStartedMessage(SeatReserved domainEvent, string correlationId)
    {
        var messageId = Guid.NewGuid();
        var integrationEvent = new ReserveSeatStartedV1(
            MessageId: messageId,
            CorrelationId: correlationId,
            BookingId: domainEvent.BookingId,
            EventId: domainEvent.EventId.Value,
            CustomerId: domainEvent.CustomerId.Value,
            SeatNumber: domainEvent.SeatNumber.Value,
            OccurredOnUtc: domainEvent.OccurredOnUtc);

        return new OutboxMessageEntity
        {
            Id = messageId,
            Type = nameof(ReserveSeatStartedV1),
            RoutingKey = "booking.reserve-seat.started.v1",
            CorrelationId = correlationId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions)
        };
    }

    private static OutboxMessageEntity CreateBookingStartedAnalyticsMessage(SeatReserved domainEvent, string correlationId)
    {
        var messageId = Guid.NewGuid();
        var analyticsEvent = new BookingStartedAnalyticsEvent(
            domainEvent.EventId.Value,
            domainEvent.CustomerId.Value,
            domainEvent.OccurredOnUtc);

        return new OutboxMessageEntity
        {
            Id = messageId,
            Type = nameof(BookingStartedAnalyticsEvent),
            RoutingKey = "booking.analytics.started",
            CorrelationId = correlationId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Payload = JsonSerializer.Serialize(analyticsEvent, SerializerOptions)
        };
    }

    private static OutboxMessageEntity CreateBookingConfirmedMessage(BookingConfirmed domainEvent, string correlationId)
    {
        var messageId = Guid.NewGuid();
        var integrationEvent = new BookingConfirmedV1(
            MessageId: messageId,
            CorrelationId: correlationId,
            BookingId: domainEvent.BookingId,
            EventId: domainEvent.EventId.Value,
            CustomerId: domainEvent.CustomerId.Value,
            OccurredOnUtc: domainEvent.OccurredOnUtc);

        return new OutboxMessageEntity
        {
            Id = messageId,
            Type = nameof(BookingConfirmedV1),
            RoutingKey = "booking.confirmed.v1",
            CorrelationId = correlationId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions)
        };
    }

    /// <summary>
    /// Translates the Event aggregate's <see cref="EventUpdated"/> domain event into the
    /// fan-out integration contract Catalog (and any future projection) consumes for cache
    /// invalidation. Uses the sentinel routing key resolved by
    /// <see cref="BookingOutboxPublisher.ResolveRoutingKey"/> so the actual wire-level key
    /// stays configurable per environment.
    /// </summary>
    private static OutboxMessageEntity CreateEventUpdatedMessage(EventUpdated domainEvent, string correlationId)
    {
        var messageId = Guid.NewGuid();
        var integrationEvent = new EventUpdatedIntegrationEventV1(
            MessageId: messageId,
            CorrelationId: correlationId,
            EventId: domainEvent.EventId.Value,
            OccurredOnUtc: domainEvent.OccurredOnUtc);

        return new OutboxMessageEntity
        {
            Id = messageId,
            Type = nameof(EventUpdatedIntegrationEventV1),
            RoutingKey = "booking.event.updated",
            CorrelationId = correlationId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions)
        };
    }
}