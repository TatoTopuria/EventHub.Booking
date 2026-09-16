using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class EventSeatEntityConfiguration : IEntityTypeConfiguration<EventSeatEntity>
{
    public void Configure(EntityTypeBuilder<EventSeatEntity> builder)
    {
        builder.ToTable("event_seats");

        builder.HasKey(entity => new { entity.EventId, entity.SeatNumber });
        builder.Property(entity => entity.SeatNumber).HasMaxLength(20).IsRequired();

        // Optimistic concurrency: bumped by EventMapper.Apply when a seat changes reservation
        // state. EF Core emits the version into the WHERE clause of UPDATE statements; a stale
        // value yields zero affected rows and a DbUpdateConcurrencyException.
        builder.Property(entity => entity.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(0u);
    }
}
