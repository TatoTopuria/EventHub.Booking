using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class BookingReservedSeatEntityConfiguration : IEntityTypeConfiguration<BookingReservedSeatEntity>
{
    public void Configure(EntityTypeBuilder<BookingReservedSeatEntity> builder)
    {
        builder.ToTable("booking_reserved_seats");

        builder.HasKey(entity => new { entity.BookingId, entity.SeatNumber });
        builder.Property(entity => entity.SeatNumber).HasMaxLength(20).IsRequired();
    }
}
