using System.Text.Json;
using Booking.Service.Application.Contracts.IntegrationEvents;
using Booking.Service.Domain.DomainEvents;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Persistence.Models;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Domain;
using FluentAssertions;

namespace Booking.Service.UnitTests.Messaging;

/// <summary>
/// Pins the wire shape of every integration event the outbox emits. Catalog (and any future
/// projection) deserializes the JSON below with <see cref="JsonSerializerDefaults.Web"/>, so any
/// rename, version bump, or property reordering must show up here first — preventing the silent
/// shape-drift described in audit gap G5.
/// </summary>
public sealed class BookingOutboxMessageFactoryTests
{
    private static readonly JsonSerializerOptions WireFormat = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Create_Should_Translate_EventUpdated_To_Versioned_Integration_Event()
    {
        var eventId = EventId.New();
        var occurredOn = DateTime.UtcNow;
        var domainEvent = new EventUpdated(eventId, occurredOn);

        var messages = BookingOutboxMessageFactory.Create(
            new DomainEvent[] { domainEvent },
            correlationId: "corr-fixed-for-test");

        messages.Should().ContainSingle()
            .Which.Should().Match<OutboxMessageEntity>(message =>
                message.Type == nameof(EventUpdatedIntegrationEventV1)
                && message.RoutingKey == "booking.event.updated"
                && message.CorrelationId == "corr-fixed-for-test"
                && message.OccurredOnUtc == occurredOn);
    }

    [Fact]
    public void Create_Should_Emit_EventUpdated_Payload_That_Round_Trips_To_Shared_Contract()
    {
        var eventId = EventId.New();
        var occurredOn = DateTime.UtcNow;
        var domainEvent = new EventUpdated(eventId, occurredOn);

        var message = BookingOutboxMessageFactory.Create(
            new DomainEvent[] { domainEvent },
            correlationId: "corr-fixed-for-test").Single();

        // Round-tripping through the shared contract proves Catalog's consumer will deserialize the
        // payload without missing fields, default-value pitfalls, or property-casing mismatches.
        var deserialized = JsonSerializer.Deserialize<EventUpdatedIntegrationEventV1>(message.Payload, WireFormat);
        deserialized.Should().NotBeNull();
        deserialized!.EventId.Should().Be(eventId.Value);
        deserialized.OccurredOnUtc.Should().Be(occurredOn);
        deserialized.CorrelationId.Should().Be("corr-fixed-for-test");
        deserialized.MessageId.Should().Be(message.Id, "the message envelope id must match the integration event id");
    }

    [Fact]
    public void Create_Should_Generate_Correlation_Id_When_None_Provided()
    {
        var domainEvent = new EventUpdated(EventId.New(), DateTime.UtcNow);

        var message = BookingOutboxMessageFactory.Create(new DomainEvent[] { domainEvent }, correlationId: null)
            .Single();

        message.CorrelationId.Should().NotBeNullOrWhiteSpace(
            "every outbox row must carry a correlation id so downstream logs can be traced across services");
    }

    [Fact]
    public void Create_Should_Emit_BookingStartedAnalytics_Message_For_SeatReserved()
    {
        var occurredOn = DateTime.UtcNow;
        var domainEvent = new SeatReserved(
            Guid.NewGuid(),
            EventId.New(),
            CustomerId.Create(Guid.NewGuid()).Value,
            SeatNumber.Create("A1").Value,
            occurredOn);

        var analyticsMessage = BookingOutboxMessageFactory.Create(
                new DomainEvent[] { domainEvent },
                correlationId: "corr-fixed-for-test")
            .Single(message => message.Type == nameof(BookingStartedAnalyticsEvent));

        analyticsMessage.RoutingKey.Should().Be("booking.analytics.started");
        analyticsMessage.CorrelationId.Should().Be("corr-fixed-for-test");
        analyticsMessage.OccurredOnUtc.Should().Be(occurredOn);

        var payload = JsonSerializer.Deserialize<BookingStartedAnalyticsEvent>(analyticsMessage.Payload, WireFormat);
        payload.Should().NotBeNull();
        payload!.EventId.Should().Be(domainEvent.EventId.Value);
        payload.UserId.Should().Be(domainEvent.CustomerId.Value);
        payload.StartedAtUtc.Should().Be(occurredOn);
    }

    [Fact]
    public void Create_Should_Skip_Unknown_Domain_Event_Kinds_Silently()
    {
        // Negative path: an aggregate that grows a new domain event before the factory learns to
        // translate it should NOT crash the outbox — it should just no-op. This protects the saga
        // pipeline from being held back by a half-done feature branch.
        var unknown = new UnknownDomainEvent(DateTime.UtcNow);

        var messages = BookingOutboxMessageFactory.Create(
            new DomainEvent[] { unknown },
            correlationId: "corr-fixed-for-test");

        messages.Should().BeEmpty();
    }

    private sealed record UnknownDomainEvent(DateTime OccurredOnUtc) : DomainEvent(OccurredOnUtc);
}
