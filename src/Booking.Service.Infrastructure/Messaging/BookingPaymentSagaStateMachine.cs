using Booking.Service.Application.Pricing;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Persistence.Models;
using BuildingBlocks.Abstractions.Messaging;
using Booking.Service.Infrastructure.Configuration;
using MassTransit;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class BookingPaymentSagaStateMachine : MassTransitStateMachine<BookingPaymentSagaState>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly BookingSagaPaymentOptions _paymentOptions;
    private readonly IPricingStrategyResolver _pricingResolver;

    public State AwaitingPayment { get; private set; } = null!;
    public State AwaitingBookingConfirmation { get; private set; } = null!;
    public State AwaitingNotification { get; private set; } = null!;
    public State Refunding { get; private set; } = null!;
    /// <summary>
    /// Terminal state reached when <see cref="RefundTimeoutExpired"/> fires while in
    /// <see cref="Refunding"/>. The saga finalizes but the row stays in the DB for ops review
    /// — querying <c>GET /api/bookings/{bookingId}/saga-state</c> surfaces it.
    /// </summary>
    public State RefundFailed { get; private set; } = null!;

    public Event<ReserveSeatStartedV1> ReserveSeatStarted { get; private set; } = null!;
    public Event<PaymentResultReceivedV1> PaymentResultReceived { get; private set; } = null!;
    public Event<BookingConfirmedV1> BookingConfirmed { get; private set; } = null!;
    public Event<NotificationSucceededV1> NotificationSucceeded { get; private set; } = null!;
    public Event<NotificationFailedV1> NotificationFailed { get; private set; } = null!;
    public Event<RefundCompletedV1> RefundCompleted { get; private set; } = null!;
    /// <summary>
    /// Synthetic event published by <c>BookingSagaTimeoutWorker</c> when a saga has been waiting
    /// in <see cref="Refunding"/> longer than <c>BookingSagaPaymentOptions.RefundTimeoutSeconds</c>.
    /// </summary>
    public Event<RefundTimeoutExpiredV1> RefundTimeoutExpired { get; private set; } = null!;

    public BookingPaymentSagaStateMachine(
        IOptions<BookingSagaPaymentOptions> paymentOptions,
        IPricingStrategyResolver pricingResolver)
    {
        _paymentOptions = paymentOptions.Value;
        _pricingResolver = pricingResolver;
        InstanceState(instance => instance.CurrentState);

        Event(() => ReserveSeatStarted, cfg =>
        {
            cfg.CorrelateById(context => context.Message.BookingId);
            cfg.SelectId(context => context.Message.BookingId);
        });

        Event(() => PaymentResultReceived, cfg => cfg.CorrelateById(context => context.Message.BookingId));
        Event(() => BookingConfirmed, cfg => cfg.CorrelateById(context => context.Message.BookingId));
        Event(() => NotificationSucceeded, cfg => cfg.CorrelateById(context => context.Message.BookingId));
        Event(() => NotificationFailed, cfg => cfg.CorrelateById(context => context.Message.BookingId));
        Event(() => RefundCompleted, cfg => cfg.CorrelateById(context => context.Message.BookingId));
        Event(() => RefundTimeoutExpired, cfg => cfg.CorrelateById(context => context.Message.BookingId));

        Initially(
            When(ReserveSeatStarted)
                .Then(context =>
                {
                    var payment = ResolvePayment(
                        context.Message.EventId,
                        context.Message.CustomerId,
                        context.Message.BookingId);

                    context.Saga.CorrelationId = context.Message.BookingId;
                    context.Saga.BookingId = context.Message.BookingId;
                    context.Saga.EventId = context.Message.EventId;
                    context.Saga.CustomerId = context.Message.CustomerId;
                    context.Saga.SeatNumber = context.Message.SeatNumber;
                    context.Saga.ChargeAmount = payment.Amount;
                    context.Saga.ChargeCurrency = payment.Currency;
                    context.Saga.ChargeProvider = payment.Provider;
                    context.Saga.ChargeIdempotencyKey = payment.IdempotencyKey;
                    context.Saga.ChargeMetadataJson = JsonSerializer.Serialize(payment.Metadata, SerializerOptions);
                    context.Saga.CreatedAtUtc = context.Message.OccurredOnUtc;
                    context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc;
                })
                .Publish(context => new ChargePaymentRequestedV1(
                    MessageId: NewId.NextGuid(),
                    CorrelationId: context.Message.CorrelationId,
                    BookingId: context.Message.BookingId,
                    CustomerId: context.Message.CustomerId,
                    Amount: context.Saga.ChargeAmount,
                    Currency: context.Saga.ChargeCurrency,
                    Provider: context.Saga.ChargeProvider,
                    IdempotencyKey: context.Saga.ChargeIdempotencyKey,
                    Metadata: DeserializeMetadata(context.Saga.ChargeMetadataJson),
                    OccurredOnUtc: DateTime.UtcNow))
                .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            When(PaymentResultReceived, context => string.Equals(context.Message.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                .Then(context =>
                {
                    context.Saga.PaymentIntentId = context.Message.PaymentIntentId;
                    context.Saga.PaymentStatus = context.Message.Status;
                    context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc;
                })
                .Publish(context => new ConfirmBookingRequestedV1(
                    MessageId: NewId.NextGuid(),
                    CorrelationId: context.Message.CorrelationId,
                    BookingId: context.Message.BookingId,
                    CustomerId: context.Saga.CustomerId,
                    PaidAmount: context.Saga.ChargeAmount,
                    Currency: context.Saga.ChargeCurrency,
                    OccurredOnUtc: DateTime.UtcNow))
                .TransitionTo(AwaitingBookingConfirmation),

            When(PaymentResultReceived, context => !string.Equals(context.Message.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                .Then(context =>
                {
                    context.Saga.PaymentIntentId = context.Message.PaymentIntentId;
                    context.Saga.PaymentStatus = context.Message.Status;
                    context.Saga.FailureReason = context.Message.Error;
                    context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc;
                })
                .Publish(context => new CancelBookingRequestedV1(
                    MessageId: NewId.NextGuid(),
                    CorrelationId: context.Message.CorrelationId,
                    BookingId: context.Message.BookingId,
                    Reason: context.Message.Error ?? "PaymentFailed",
                    OccurredOnUtc: DateTime.UtcNow))
                .Finalize());

        During(AwaitingBookingConfirmation,
            When(BookingConfirmed)
                .Then(context => context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc)
                .TransitionTo(AwaitingNotification));

        // F7 — Notification outcome arrives either as success (saga finalizes happily) or as
        // exhausted-retries failure (saga compensates by issuing a refund).
        During(AwaitingNotification,
            When(NotificationSucceeded)
                .Then(context =>
                {
                    context.Saga.NotificationStatus = "Succeeded";
                    context.Saga.NotificationCompletedAtUtc = context.Message.OccurredOnUtc;
                    context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc;
                })
                .Finalize(),

            When(NotificationFailed)
                .Then(context =>
                {
                    context.Saga.NotificationStatus = "Failed";
                    context.Saga.NotificationFailureReason = context.Message.Reason;
                    context.Saga.NotificationCompletedAtUtc = context.Message.OccurredOnUtc;
                    context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc;
                    // F5: stamp the refund entry time so BookingSagaTimeoutWorker can compare
                    // against (now - RefundTimeoutSeconds) to find sagas hanging waiting for
                    // Payment.Service to respond with RefundCompletedV1.
                    context.Saga.RefundRequestedAtUtc = DateTime.UtcNow;
                })
                .Publish(context => new RefundRequestedV1(
                    MessageId: NewId.NextGuid(),
                    CorrelationId: context.Message.CorrelationId,
                    BookingId: context.Message.BookingId,
                    PaymentIntentId: context.Saga.PaymentIntentId ?? Guid.Empty,
                    Reason: context.Message.Reason,
                    OccurredOnUtc: DateTime.UtcNow))
                .TransitionTo(Refunding));

        // F5 — Refunding has two terminal branches with deliberately asymmetric finalization:
        //   • RefundCompleted (happy) → Finalize, which deletes the row via
        //     SetCompletedWhenFinalized below. There is nothing to triage; the booking is whole.
        //   • RefundTimeoutExpired (escalation) → TransitionTo(RefundFailed) WITHOUT Finalize.
        //     The row persists so ops can hit GET /api/bookings/{id}/saga-state and see what
        //     happened. RefundFailed is dormant — no handlers in that state — so a late
        //     RefundCompleted is dropped by the dispatcher (lands in _skipped) rather than
        //     re-opening a terminal saga.
        During(Refunding,
            When(RefundCompleted)
                .Then(context =>
                {
                    context.Saga.RefundStatus = context.Message.Status;
                    context.Saga.RefundReference = context.Message.ProviderRefundReference;
                    context.Saga.RefundCompletedAtUtc = context.Message.OccurredOnUtc;
                    context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc;
                })
                .Finalize(),

            When(RefundTimeoutExpired)
                .Then(context =>
                {
                    context.Saga.RefundStatus = "TimedOut";
                    context.Saga.RefundTimeoutElapsedSeconds = context.Message.ElapsedSeconds;
                    context.Saga.RefundCompletedAtUtc = context.Message.OccurredOnUtc;
                    context.Saga.UpdatedAtUtc = context.Message.OccurredOnUtc;
                })
                .TransitionTo(RefundFailed));

        SetCompletedWhenFinalized();
    }

    private ResolvedPaymentSettings ResolvePayment(Guid eventId, Guid customerId, Guid bookingId)
    {
        decimal baseAmount;
        string currency;
        string provider;
        IReadOnlyDictionary<string, string> metadata;
        DateTimeOffset eventStartsAtUtc;

        if (_paymentOptions.EventPrices.TryGetValue(eventId.ToString("D"), out var configured))
        {
            baseAmount = configured.Amount;
            currency = configured.Currency;
            provider = configured.Provider;
            metadata = configured.Metadata.Count == 0
                ? new Dictionary<string, string>(_paymentOptions.DefaultMetadata, StringComparer.OrdinalIgnoreCase)
                : configured.Metadata;
            // Sentinel: when not configured per event, push the date so far out that early-bird never fires.
            eventStartsAtUtc = configured.StartsAtUtc ?? DateTimeOffset.MaxValue;
        }
        else
        {
            baseAmount = _paymentOptions.DefaultAmount;
            currency = _paymentOptions.DefaultCurrency;
            provider = _paymentOptions.DefaultProvider;
            metadata = new Dictionary<string, string>(_paymentOptions.DefaultMetadata, StringComparer.OrdinalIgnoreCase);
            eventStartsAtUtc = DateTimeOffset.MaxValue;
        }

        var baseMoneyResult = Money.Create(baseAmount, currency);
        if (baseMoneyResult.IsFailure)
        {
            // Invalid configuration. Fall back to base amount without strategies running so the saga
            // can still proceed with a deterministic charge attempt rather than throwing here.
            return new ResolvedPaymentSettings(
                baseAmount,
                currency,
                provider,
                $"booking:{bookingId:N}:charge",
                metadata);
        }

        var customerIdResult = CustomerId.Create(customerId);
        var eventIdResult = EventId.Create(eventId);
        if (customerIdResult.IsFailure || eventIdResult.IsFailure)
        {
            return new ResolvedPaymentSettings(
                baseAmount,
                currency,
                provider,
                $"booking:{bookingId:N}:charge",
                metadata);
        }

        var pricingContext = new BookingPricingContext(
            EventId: eventIdResult.Value,
            CustomerId: customerIdResult.Value,
            EventStartsAtUtc: eventStartsAtUtc,
            BaseAmount: baseMoneyResult.Value,
            SeatCount: 1,
            Metadata: metadata);

        var decision = _pricingResolver.Resolve(pricingContext);

        return new ResolvedPaymentSettings(
            decision.FinalAmount.Amount,
            decision.FinalAmount.Currency,
            provider,
            $"booking:{bookingId:N}:charge",
            metadata);
    }

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson, SerializerOptions)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ResolvedPaymentSettings(
        decimal Amount,
        string Currency,
        string Provider,
        string IdempotencyKey,
        IReadOnlyDictionary<string, string> Metadata);
}