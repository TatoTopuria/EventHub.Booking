using Booking.Service.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Service.Infrastructure.Persistence.Configuration;

public sealed class BookingPaymentSagaStateConfiguration : IEntityTypeConfiguration<BookingPaymentSagaState>
{
    public void Configure(EntityTypeBuilder<BookingPaymentSagaState> builder)
    {
        builder.ToTable("booking_payment_saga_states");

        builder.HasKey(entity => entity.CorrelationId);

        builder.Property(entity => entity.CurrentState).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.SeatNumber).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ChargeCurrency).HasMaxLength(8).IsRequired();
        builder.Property(entity => entity.ChargeProvider).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ChargeIdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.ChargeMetadataJson).IsRequired();
        builder.Property(entity => entity.PaymentStatus).HasMaxLength(64);
        builder.Property(entity => entity.FailureReason).HasMaxLength(512);

        builder.Property(entity => entity.CreatedAtUtc).IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc).IsRequired();

        // F7 refund-compensation columns.
        builder.Property(entity => entity.NotificationStatus).HasMaxLength(32);
        builder.Property(entity => entity.NotificationFailureReason).HasMaxLength(512);
        builder.Property(entity => entity.RefundStatus).HasMaxLength(32);
        builder.Property(entity => entity.RefundReference).HasMaxLength(128);

        builder.HasIndex(entity => entity.BookingId).IsUnique();

        // F5 refund-timeout columns. Composite index on (CurrentState, RefundRequestedAtUtc) so
        // the BookingSagaTimeoutWorker query "give me stale Refunding sagas" can scan a covering
        // index instead of the heap once the saga state table grows.
        builder.HasIndex(entity => new { entity.CurrentState, entity.RefundRequestedAtUtc });
    }
}