using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Contracts.Persistence;

public interface IEventRepository
{
    Task AddAsync(Event eventAggregate, CancellationToken cancellationToken = default);

    Task UpdateAsync(Event eventAggregate, CancellationToken cancellationToken = default);

    Task<Event?> GetByIdAsync(EventId eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Event>> ListAsync(
        DateTime? lastStartsAtUtc,
        Guid? lastEventId,
        int take,
        CancellationToken cancellationToken = default);
}
