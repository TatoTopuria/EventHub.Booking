using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Type).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.RoutingKey).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Payload).IsRequired();
        builder.Property(entity => entity.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.OccurredOnUtc).IsRequired();

        builder.HasIndex(entity => entity.ProcessedOnUtc);
        builder.HasIndex(entity => new { entity.ProcessedOnUtc, entity.OccurredOnUtc });
    }
}