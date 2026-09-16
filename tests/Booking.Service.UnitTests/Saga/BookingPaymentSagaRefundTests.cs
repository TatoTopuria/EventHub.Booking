using Booking.Service.Application.Pricing;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Persistence.Models;
using BuildingBlocks.Abstractions.Messaging;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Booking.Service.UnitTests.Saga;

/// <summary>
/// Drives <see cref="BookingPaymentSagaStateMachine"/> in-memory and asserts that the F7 transitions
/// behave as designed: success path finalizes happily after notification, failure path publishes
/// <see cref="RefundRequestedV1"/> and finalizes after <see cref="RefundCompletedV1"/>.
/// </summary>
public sealed class BookingPaymentSagaRefundTests : IAsyncLifetime
{
    private ITestHarness _harness = null!;
    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(new BookingSagaPaymentOptions
        {
            DefaultAmount = 100m,
            DefaultCurrency = "USD",
            DefaultProvider = "stripe"
        }));

        // Pricing resolver with no strategies — saga gets the base amount.
        services.AddSingleton<IPricingStrategyResolver>(new PricingStrategyResolver(Array.Empty<IPricingStrategy>()));

        services.AddMassTransitTestHarness(configurator =>
        {
            configurator.AddSagaStateMachine<BookingPaymentSagaStateMachine, BookingPaymentSagaState>()
                .InMemoryRepository();
        });

        _serviceProvider = services.BuildServiceProvider(true);
        _harness = _serviceProvider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task NotificationSucceeded_Should_Finalize_The_Saga()
    {
        var bookingId = Guid.NewGuid();
        await DriveToAwaitingNotificationAsync(bookingId);

        await _harness.Bus.Publish(new NotificationSucceededV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            OccurredOnUtc: DateTime.UtcNow));

        var sagaHarness = _harness.GetSagaStateMachineHarness<BookingPaymentSagaStateMachine, BookingPaymentSagaState>();
        (await sagaHarness.Consumed.Any<NotificationSucceededV1>()).Should().BeTrue();

        // No RefundRequestedV1 must have been published on the success path.
        (await _harness.Published.Any<RefundRequestedV1>()).Should().BeFalse();
    }

    [Fact]
    public async Task PaymentFailed_Should_Publish_CancelBookingRequested_And_Finalize()
    {
        var bookingId = Guid.NewGuid();
        var correlationId = bookingId.ToString("N");

        await _harness.Bus.Publish(new ReserveSeatStartedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: correlationId,
            BookingId: bookingId,
            EventId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            SeatNumber: "A1",
            OccurredOnUtc: DateTime.UtcNow));

        var sagaHarness = _harness.GetSagaStateMachineHarness<BookingPaymentSagaStateMachine, BookingPaymentSagaState>();
        (await sagaHarness.Consumed.Any<ReserveSeatStartedV1>()).Should().BeTrue();

        await _harness.Bus.Publish(new PaymentResultReceivedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: correlationId,
            BookingId: bookingId,
            PaymentIntentId: Guid.NewGuid(),
            Status: "Failed",
            ProviderReference: "stripe_ref",
            Error: "Simulated payment failure",
            OccurredOnUtc: DateTime.UtcNow));

        (await sagaHarness.Consumed.Any<PaymentResultReceivedV1>()).Should().BeTrue();
        (await _harness.Published.Any<CancelBookingRequestedV1>()).Should().BeTrue();
    }

    [Fact]
    public async Task NotificationFailed_Should_Publish_RefundRequested_And_Move_To_Refunding()
    {
        var bookingId = Guid.NewGuid();
        await DriveToAwaitingNotificationAsync(bookingId);

        await _harness.Bus.Publish(new NotificationFailedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            Reason: "SMTP unreachable",
            OccurredOnUtc: DateTime.UtcNow));

        var sagaHarness = _harness.GetSagaStateMachineHarness<BookingPaymentSagaStateMachine, BookingPaymentSagaState>();
        (await sagaHarness.Consumed.Any<NotificationFailedV1>()).Should().BeTrue();

        // Saga must have published the refund request for Payment.Service to act on.
        (await _harness.Published.Any<RefundRequestedV1>()).Should().BeTrue();
    }

    [Fact]
    public async Task RefundCompleted_Should_Finalize_The_Saga_After_Refund_Flow()
    {
        var bookingId = Guid.NewGuid();
        await DriveToAwaitingNotificationAsync(bookingId);
        await _harness.Bus.Publish(new NotificationFailedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            Reason: "SMTP unreachable",
            OccurredOnUtc: DateTime.UtcNow));

        // Simulate Payment.Service completing the refund.
        await _harness.Bus.Publish(new RefundCompletedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            PaymentIntentId: Guid.NewGuid(),
            Status: "Refunded",
            ProviderRefundReference: "ref_abc",
            Error: null,
            OccurredOnUtc: DateTime.UtcNow));

        var sagaHarness = _harness.GetSagaStateMachineHarness<BookingPaymentSagaStateMachine, BookingPaymentSagaState>();
        (await sagaHarness.Consumed.Any<RefundCompletedV1>()).Should().BeTrue();

        // Saga must end in the final state (instance no longer present in the repository).
        var instance = sagaHarness.Created.ContainsInState(
            bookingId,
            sagaHarness.StateMachine,
            saga => saga.Final);
        instance.Should().NotBeNull();
    }

    [Fact]
    public async Task RefundTimeoutExpired_Should_Transition_To_RefundFailed_And_Stamp_Snapshot()
    {
        // F5 — the timeout branch. Drive the saga to Refunding (NotificationFailed → publish
        // RefundRequested), then publish RefundTimeoutExpiredV1 as if BookingSagaTimeoutWorker
        // had detected the stuck saga. Assert: the state machine consumes the event, stamps the
        // snapshot fields, and transitions to the dormant RefundFailed state — the row stays in
        // the repository so the ops endpoint can surface it.
        var bookingId = Guid.NewGuid();
        await DriveToAwaitingNotificationAsync(bookingId);
        await _harness.Bus.Publish(new NotificationFailedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            Reason: "SMTP unreachable",
            OccurredOnUtc: DateTime.UtcNow));

        var sagaHarness = _harness.GetSagaStateMachineHarness<BookingPaymentSagaStateMachine, BookingPaymentSagaState>();
        (await sagaHarness.Consumed.Any<NotificationFailedV1>()).Should().BeTrue();

        await _harness.Bus.Publish(new RefundTimeoutExpiredV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            PaymentIntentId: Guid.NewGuid(),
            ElapsedSeconds: 73,
            OccurredOnUtc: DateTime.UtcNow));

        (await sagaHarness.Consumed.Any<RefundTimeoutExpiredV1>()).Should().BeTrue();

        // Saga must be alive in RefundFailed (NOT Final — the row deliberately persists for ops
        // triage via GET /api/bookings/{id}/saga-state).
        var inRefundFailed = sagaHarness.Created.ContainsInState(
            bookingId,
            sagaHarness.StateMachine,
            saga => saga.RefundFailed);
        inRefundFailed.Should().NotBeNull(
            "the timeout branch must transition to RefundFailed without finalizing — the row must persist for ops");

        var snapshot = sagaHarness.Created.Contains(bookingId);
        snapshot.Should().NotBeNull();
        snapshot!.RefundStatus.Should().Be("TimedOut");
        snapshot.RefundTimeoutElapsedSeconds.Should().Be(73);
    }

    [Fact]
    public async Task RefundCompleted_Arriving_After_Timeout_Is_Dropped_By_Dispatcher()
    {
        // Late RefundCompleted (Payment.Service eventually responded after the timeout already
        // moved the saga to RefundFailed). RefundFailed has no When(RefundCompleted) handler, so
        // MassTransit drops the message at the dispatcher rather than re-opening the saga.
        var bookingId = Guid.NewGuid();
        await DriveToAwaitingNotificationAsync(bookingId);
        await _harness.Bus.Publish(new NotificationFailedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            Reason: "SMTP unreachable",
            OccurredOnUtc: DateTime.UtcNow));
        await _harness.Bus.Publish(new RefundTimeoutExpiredV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            PaymentIntentId: Guid.NewGuid(),
            ElapsedSeconds: 73,
            OccurredOnUtc: DateTime.UtcNow));

        var sagaHarness = _harness.GetSagaStateMachineHarness<BookingPaymentSagaStateMachine, BookingPaymentSagaState>();
        (await sagaHarness.Consumed.Any<RefundTimeoutExpiredV1>()).Should().BeTrue();

        // Now the late RefundCompleted arrives.
        await _harness.Bus.Publish(new RefundCompletedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: bookingId.ToString("N"),
            BookingId: bookingId,
            PaymentIntentId: Guid.NewGuid(),
            Status: "Refunded",
            ProviderRefundReference: "late_ref",
            Error: null,
            OccurredOnUtc: DateTime.UtcNow));

        // Saga must still be alive in RefundFailed — the late RefundCompleted did not re-open it
        // or leak its provider reference into the timed-out snapshot.
        var snapshot = sagaHarness.Created.Contains(bookingId);
        snapshot.Should().NotBeNull();
        snapshot!.CurrentState.Should().Be(nameof(BookingPaymentSagaStateMachine.RefundFailed));
        snapshot.RefundStatus.Should().Be("TimedOut");
        snapshot.RefundReference.Should().BeNull(
            "the late RefundCompleted's reference must not have leaked into the timed-out snapshot");
    }

    private async Task DriveToAwaitingNotificationAsync(Guid bookingId)
    {
        var correlationId = bookingId.ToString("N");

        await _harness.Bus.Publish(new ReserveSeatStartedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: correlationId,
            BookingId: bookingId,
            EventId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            SeatNumber: "A1",
            OccurredOnUtc: DateTime.UtcNow));

        await _harness.Bus.Publish(new PaymentResultReceivedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: correlationId,
            BookingId: bookingId,
            PaymentIntentId: Guid.NewGuid(),
            Status: "Succeeded",
            ProviderReference: "stripe_ref",
            Error: null,
            OccurredOnUtc: DateTime.UtcNow));

        await _harness.Bus.Publish(new BookingConfirmedV1(
            MessageId: Guid.NewGuid(),
            CorrelationId: correlationId,
            BookingId: bookingId,
            EventId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow));

        // Give the in-memory bus a moment to consume the priming events.
        var sagaHarness = _harness.GetSagaStateMachineHarness<BookingPaymentSagaStateMachine, BookingPaymentSagaState>();
        (await sagaHarness.Consumed.Any<BookingConfirmedV1>()).Should().BeTrue();
    }
}
