using BuildingBlocks.Domain;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class DomainEventCollector
{
    private readonly List<DomainEvent> _domainEvents = [];

    public void Collect(IReadOnlyCollection<DomainEvent> domainEvents)
    {
        if (domainEvents.Count == 0)
        {
            return;
        }

        _domainEvents.AddRange(domainEvents);
    }

    public IReadOnlyCollection<DomainEvent> Drain()
    {
        var items = _domainEvents.ToArray();
        _domainEvents.Clear();
        return items;
    }
}