using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class BookingEntityConfiguration : IEntityTypeConfiguration<BookingEntity>
{
    public void Configure(EntityTypeBuilder<BookingEntity> builder)
    {
        builder.ToTable("bookings");

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Status).IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).IsRequired();
        builder.Property(entity => entity.PaymentCurrency).HasMaxLength(3);

        builder.HasIndex(entity => entity.EventId);
        builder.HasIndex(entity => entity.CustomerId);
        builder.HasIndex(entity => entity.Status);
        builder.HasIndex(entity => new { entity.CustomerId, entity.CreatedAtUtc, entity.Id });

        builder.HasMany(entity => entity.ReservedSeats)
            .WithOne(entity => entity.Booking)
            .HasForeignKey(entity => entity.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
