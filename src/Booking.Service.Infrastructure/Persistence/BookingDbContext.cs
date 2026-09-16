using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace Booking.Service.Infrastructure.Persistence;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<EventEntity> Events => Set<EventEntity>();

    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    public DbSet<BookingPaymentSagaState> BookingPaymentSagaStates => Set<BookingPaymentSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
    }
}
