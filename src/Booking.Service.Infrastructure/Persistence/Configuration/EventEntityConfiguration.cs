using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class EventEntityConfiguration : IEntityTypeConfiguration<EventEntity>
{
    public void Configure(EntityTypeBuilder<EventEntity> builder)
    {
        builder.ToTable("events");

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Organizer).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Status).IsRequired();
        builder.Property(entity => entity.StartsAtUtc).IsRequired();
        builder.Property(entity => entity.EndsAtUtc).IsRequired();

        builder.HasIndex(entity => entity.Status);
        builder.HasIndex(entity => new { entity.StartsAtUtc, entity.Id });

        builder.HasMany(entity => entity.Seats)
            .WithOne(entity => entity.Event)
            .HasForeignKey(entity => entity.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(entity => entity.ReservedSeats)
            .WithOne(entity => entity.Event)
            .HasForeignKey(entity => entity.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
