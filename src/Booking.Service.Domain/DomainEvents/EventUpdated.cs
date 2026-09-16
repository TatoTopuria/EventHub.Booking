using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Domain;

namespace Booking.Service.Domain.DomainEvents;

/// <summary>
/// Raised when the canonical state of an <see cref="Aggregates.Event"/> changes in a way the
/// outside world cares about — schedule moves, organizer changes, lifecycle transitions.
/// Translated to <see cref="BuildingBlocks.Abstractions.Messaging.EventUpdatedIntegrationEventV1"/>
/// by the outbox factory and fanned out so subscribers (Catalog cache invalidator, future
/// projections) can react.
/// </summary>
public sealed record EventUpdated(
    EventId EventId,
    DateTime OccurredOnUtc) : DomainEvent(OccurredOnUtc);
