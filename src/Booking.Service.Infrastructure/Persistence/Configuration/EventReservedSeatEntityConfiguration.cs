using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class EventReservedSeatEntityConfiguration : IEntityTypeConfiguration<EventReservedSeatEntity>
{
    public void Configure(EntityTypeBuilder<EventReservedSeatEntity> builder)
    {
        builder.ToTable("event_reserved_seats");

        builder.HasKey(entity => new { entity.EventId, entity.SeatNumber });
        builder.Property(entity => entity.SeatNumber).HasMaxLength(20).IsRequired();
    }
}
