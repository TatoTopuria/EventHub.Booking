using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

using BookingAggregate = Booking.Service.Domain.Aggregates.Booking;

namespace Booking.Service.Infrastructure.Persistence;

public sealed class EfBookingRepository(BookingDbContext dbContext, DomainEventCollector domainEventCollector) : IBookingRepository
{
    public async Task AddAsync(BookingAggregate booking, CancellationToken cancellationToken = default)
    {
        domainEventCollector.Collect(booking.DomainEvents);
        booking.ClearDomainEvents();
        await dbContext.Bookings.AddAsync(BookingMapper.ToEntity(booking), cancellationToken);
    }

    public async Task UpdateAsync(BookingAggregate booking, CancellationToken cancellationToken = default)
    {
        domainEventCollector.Collect(booking.DomainEvents);
        booking.ClearDomainEvents();

        var existingEntity = dbContext.Bookings.Local.FirstOrDefault(entity => entity.Id == booking.Id)
            ?? await dbContext.Bookings
                .Include(entity => entity.ReservedSeats)
                .FirstOrDefaultAsync(entity => entity.Id == booking.Id, cancellationToken);

        if (existingEntity is null)
        {
            await AddAsync(booking, cancellationToken);
            return;
        }

        BookingMapper.Apply(booking, existingEntity);
    }

    public async Task<BookingAggregate?> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Bookings
            .AsNoTracking()
            .Include(bookingEntity => bookingEntity.ReservedSeats)
            .FirstOrDefaultAsync(bookingEntity => bookingEntity.Id == bookingId, cancellationToken);

        return entity is null ? null : BookingMapper.ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<BookingAggregate>> GetByCustomerIdAsync(
        CustomerId customerId,
        DateTime? lastCreatedAtUtc,
        Guid? lastBookingId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(take, 1, 100);

        var query = dbContext.Bookings
            .AsNoTracking()
            .Include(bookingEntity => bookingEntity.ReservedSeats)
            .Where(bookingEntity => bookingEntity.CustomerId == customerId.Value)
            .OrderByDescending(bookingEntity => bookingEntity.CreatedAtUtc)
            .ThenByDescending(bookingEntity => bookingEntity.Id)
            .AsQueryable();

        if (lastCreatedAtUtc.HasValue && lastBookingId.HasValue)
        {
            query = query.Where(bookingEntity =>
                bookingEntity.CreatedAtUtc < lastCreatedAtUtc.Value
                || (bookingEntity.CreatedAtUtc == lastCreatedAtUtc.Value && bookingEntity.Id.CompareTo(lastBookingId.Value) < 0));
        }

        var entities = await query.Take(pageSize).ToListAsync(cancellationToken);
        return entities.Select(BookingMapper.ToDomain).ToArray();
    }

    public async Task<IReadOnlyCollection<BookingAggregate>> GetExpiringPendingAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(batchSize, 1, 500);
        var pendingStatus = (int)BookingStatus.PendingPayment;

        var entities = await dbContext.Bookings
            .AsNoTracking()
            .Include(bookingEntity => bookingEntity.ReservedSeats)
            .Where(bookingEntity =>
                bookingEntity.Status == pendingStatus
                && bookingEntity.CreatedAtUtc < cutoffUtc)
            .OrderBy(bookingEntity => bookingEntity.CreatedAtUtc)
            .ThenBy(bookingEntity => bookingEntity.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return entities.Select(BookingMapper.ToDomain).ToArray();
    }
}
