using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class InboxMessageEntityConfiguration : IEntityTypeConfiguration<InboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<InboxMessageEntity> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.MessageId).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ProcessingStartedAtUtc);
        builder.Property(entity => entity.ProcessedAtUtc);

        builder.HasIndex(entity => entity.MessageId).IsUnique();
    }
}