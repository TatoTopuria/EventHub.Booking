using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Booking.Service.Infrastructure.Persistence;

public sealed class EfEventRepository(BookingDbContext dbContext, DomainEventCollector domainEventCollector) : IEventRepository
{
    public async Task AddAsync(Event eventAggregate, CancellationToken cancellationToken = default)
    {
        // Same pattern as EfBookingRepository — drain the aggregate's domain events into the
        // scoped collector so EfUnitOfWork translates them into outbox rows in the same
        // transaction as the EF SaveChanges. Without this, EventUpdated raised by
        // Reschedule/ChangeOrganizer is silently dropped before the outbox factory runs.
        domainEventCollector.Collect(eventAggregate.DomainEvents);
        eventAggregate.ClearDomainEvents();
        await dbContext.Events.AddAsync(EventMapper.ToEntity(eventAggregate), cancellationToken);
    }

    public async Task UpdateAsync(Event eventAggregate, CancellationToken cancellationToken = default)
    {
        domainEventCollector.Collect(eventAggregate.DomainEvents);
        eventAggregate.ClearDomainEvents();

        var existingEntity = dbContext.Events.Local.FirstOrDefault(entity => entity.Id == eventAggregate.Id.Value)
            ?? await dbContext.Events
                .Include(entity => entity.Seats)
                .Include(entity => entity.ReservedSeats)
                .FirstOrDefaultAsync(entity => entity.Id == eventAggregate.Id.Value, cancellationToken);

        if (existingEntity is null)
        {
            await dbContext.Events.AddAsync(EventMapper.ToEntity(eventAggregate), cancellationToken);
            return;
        }

        EventMapper.Apply(eventAggregate, existingEntity);
    }

    public async Task<Event?> GetByIdAsync(EventId eventId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Events
            .AsNoTracking()
            .Include(eventEntity => eventEntity.Seats)
            .Include(eventEntity => eventEntity.ReservedSeats)
            .FirstOrDefaultAsync(eventEntity => eventEntity.Id == eventId.Value, cancellationToken);

        return entity is null ? null : EventMapper.ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<Event>> ListAsync(
        DateTime? lastStartsAtUtc,
        Guid? lastEventId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(take, 1, 100);

        var query = dbContext.Events
            .AsNoTracking()
            .Include(eventEntity => eventEntity.Seats)
            .Include(eventEntity => eventEntity.ReservedSeats)
            .OrderBy(eventEntity => eventEntity.StartsAtUtc)
            .ThenBy(eventEntity => eventEntity.Id)
            .AsQueryable();

        if (lastStartsAtUtc.HasValue && lastEventId.HasValue)
        {
            query = query.Where(eventEntity =>
                eventEntity.StartsAtUtc > lastStartsAtUtc.Value
                || (eventEntity.StartsAtUtc == lastStartsAtUtc.Value && eventEntity.Id.CompareTo(lastEventId.Value) > 0));
        }

        var entities = await query.Take(pageSize).ToListAsync(cancellationToken);
        return entities.Select(EventMapper.ToDomain).ToArray();
    }
}
