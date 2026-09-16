using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Infrastructure.Messaging;
using Microsoft.AspNetCore.Http;

namespace Booking.Service.Infrastructure.Persistence;

public sealed class EfUnitOfWork(
    BookingDbContext dbContext,
    DomainEventCollector domainEventCollector,
    IHttpContextAccessor httpContextAccessor) : IBookingUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var correlationId = httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString();
        var outboxMessages = BookingOutboxMessageFactory.Create(domainEventCollector.Drain(), correlationId);

        if (outboxMessages.Count > 0)
        {
            await dbContext.OutboxMessages.AddRangeAsync(outboxMessages, cancellationToken);
        }

        return await dbContext.SaveChangesAsync(cancellationToken);
    }
}
