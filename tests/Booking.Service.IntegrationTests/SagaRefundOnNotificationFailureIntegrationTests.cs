using System.Net;
using System.Net.Http.Json;
using Booking.Service.Api.Contracts;
using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Application.Bookings.Queries.GetUserBookings;
using Booking.Service.Application.Events.Queries.ListEvents;
using Booking.Service.Infrastructure.Saga;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Security;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Service.IntegrationTests;

/// <summary>
/// F7 end-to-end test — drives the saga past ChargePayment by impersonating Payment.Service, then
/// publishes <see cref="NotificationFailedV1"/> as if Notification.Service had exhausted its retries,
/// and asserts that the saga publishes a <see cref="RefundRequestedV1"/> for Payment to act on.
/// </summary>
public sealed class SagaRefundOnNotificationFailureIntegrationTests(BookingApiFactory factory)
    : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task NotificationFailed_Should_Trigger_RefundRequested_From_Saga()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        // Two responders: one drives the saga past ChargePayment (mirrors the existing saga happy-path
        // test), the second waits to observe the RefundRequestedV1 the saga should publish.
        var paymentResponderBus = CreatePaymentResponder(factory);
        await paymentResponderBus.StartAsync();

        using var refundObserver = new RefundObserver();
        var refundObserverBus = CreateRefundObserverBus(factory, refundObserver);
        await refundObserverBus.StartAsync();

        try
        {
            using var client = factory.CreateClient();
            var customerId = Guid.NewGuid();
            GatewayTestAuthHeaders.AddForUser(client, customerId);

            var events = await client.GetFromJsonAsync<ListEventsResponse>("/api/events?pageSize=1");
            events.Should().NotBeNull();
            var eventId = events!.Items.Single().EventId;

            var reserveResponse = await client.PostAsJsonAsync(
                "/api/bookings/reserve",
                new ReserveSeatRequest(eventId, "A1"));
            reserveResponse.EnsureSuccessStatusCode();

            // Saga is now past the payment step and waiting for the notification outcome.
            // Wait briefly for the booking to flip to Confirmed before injecting failure, so we are
            // not racing the saga's payment-success transition.
            var confirmed = await WaitForBookingStatusAsync(client, customerId, "Confirmed", TimeSpan.FromSeconds(30));
            confirmed.Should().BeTrue();

            var bookingId = (await GetCustomerBookingsAsync(client, customerId))
                .Single(item => item.EventId == eventId).BookingId;

            await WaitForSagaStateAsync(bookingId, "AwaitingNotification", TimeSpan.FromSeconds(30));

            // Simulate Notification.Service exhausting its retry policy.
            await refundObserverBus.Publish(new NotificationFailedV1(
                MessageId: Guid.NewGuid(),
                CorrelationId: bookingId.ToString("N"),
                BookingId: bookingId,
                Reason: "Integration test simulated SMTP failure.",
                OccurredOnUtc: DateTime.UtcNow));

            var observed = await refundObserver.WaitForRefundAsync(bookingId, TimeSpan.FromSeconds(30));
            observed.Should().BeTrue("saga must publish RefundRequestedV1 in response to NotificationFailedV1");
        }
        finally
        {
            await refundObserverBus.StopAsync();
            await paymentResponderBus.StopAsync();
        }
    }

    /// <summary>
    /// F5 — Payment never replies to <see cref="RefundRequestedV1"/>. The
    /// <see cref="BookingSagaTimeoutService"/> must escalate the stuck saga via
    /// <see cref="RefundTimeoutExpiredV1"/>, the state machine must transition to
    /// <c>RefundFailed</c> and finalize, and the admin endpoint must surface the snapshot.
    /// </summary>
    [Fact]
    public async Task Refunding_Without_RefundCompleted_Should_Time_Out_And_Reach_RefundFailed()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        // Same Payment.Service responder as the happy refund test — we drive the saga past Charge
        // but DO NOT spin up an observer that responds to RefundRequestedV1. The saga sits in
        // Refunding indefinitely from Payment's perspective.
        var paymentResponderBus = CreatePaymentResponder(factory);
        await paymentResponderBus.StartAsync();

        try
        {
            using var client = factory.CreateClient();
            var customerId = Guid.NewGuid();
            GatewayTestAuthHeaders.AddForUser(client, customerId);

            var events = await client.GetFromJsonAsync<ListEventsResponse>("/api/events?pageSize=1");
            events.Should().NotBeNull();
            var eventId = events!.Items.Single().EventId;

            var reserveResponse = await client.PostAsJsonAsync(
                "/api/bookings/reserve",
                new ReserveSeatRequest(eventId, "A2"));
            reserveResponse.EnsureSuccessStatusCode();

            var confirmed = await WaitForBookingStatusAsync(client, customerId, "Confirmed", TimeSpan.FromSeconds(30));
            confirmed.Should().BeTrue();

            var bookingId = (await GetCustomerBookingsAsync(client, customerId))
                .Single(item => item.EventId == eventId).BookingId;

            // Fire Notification failure to push the saga into Refunding. Nothing on the bus is
            // listening for RefundRequestedV1, so the saga is now stranded — exactly the G6
            // failure mode F5 is designed to recover from.
            await paymentResponderBus.Publish(new NotificationFailedV1(
                MessageId: Guid.NewGuid(),
                CorrelationId: bookingId.ToString("N"),
                BookingId: bookingId,
                Reason: "Integration test simulated SMTP exhaustion.",
                OccurredOnUtc: DateTime.UtcNow));

            var inRefunding = await WaitForSagaStateAsync(bookingId, "Refunding", TimeSpan.FromSeconds(15));
            inRefunding.Should().BeTrue("saga must reach Refunding before the timeout sweep can detect it");

            // The fixture disables the polling worker so the test can drive the sweep
            // deterministically. Backdate the saga's RefundRequestedAtUtc so the worker's
            // (RefundRequestedAtUtc < cutoff) predicate fires on the first sweep, then run the
            // sweep once.
            await BackdateRefundRequestedAsync(bookingId, TimeSpan.FromMinutes(5));
            var published = await RunSagaTimeoutSweepOnceAsync();
            published.Should().BeGreaterThanOrEqualTo(1, "the backdated saga should be the one we just stranded");

            var inRefundFailed = await WaitForSagaStateAsync(bookingId, "RefundFailed", TimeSpan.FromSeconds(15));
            inRefundFailed.Should().BeTrue("the timeout branch must transition the saga to the terminal RefundFailed state");

            // Admin endpoint must surface the timeout snapshot for ops triage.
            using var admin = factory.CreateClient();
            GatewayTestAuthHeaders.AddForUser(admin, Guid.NewGuid(), EventHubRoles.Admin);

            var snapshotResponse = await admin.GetAsync($"/api/bookings/{bookingId:D}/saga-state");
            snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<BookingSagaStateSnapshot>();
            snapshot.Should().NotBeNull();
            snapshot!.RefundStatus.Should().Be("TimedOut");
            snapshot.RefundTimeoutElapsedSeconds.Should().NotBeNull().And.BeGreaterThan(0);
        }
        finally
        {
            await paymentResponderBus.StopAsync();
        }
    }

    private async Task BackdateRefundRequestedAsync(Guid bookingId, TimeSpan howFarBack)
    {
        // Direct DB write — we are simulating "the saga has been waiting too long" without
        // actually sleeping for minutes. The state machine doesn't care; it only sees the
        // synthetic RefundTimeoutExpiredV1 the worker publishes once the predicate matches.
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Booking.Service.Infrastructure.Persistence.BookingDbContext>();
        var saga = await dbContext.BookingPaymentSagaStates
            .SingleAsync(s => s.BookingId == bookingId);
        saga.RefundRequestedAtUtc = DateTime.UtcNow.Subtract(howFarBack);
        await dbContext.SaveChangesAsync();
    }

    private async Task<int> RunSagaTimeoutSweepOnceAsync()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingSagaTimeoutService>();
        return await service.RunOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// Polls the saga snapshot table for the given <paramref name="expectedState"/>. RefundFailed
    /// is a dormant terminal state that intentionally persists the row for ops triage, so we can
    /// always read it through the snapshot reader.
    /// </summary>
    private async Task<bool> WaitForSagaStateAsync(Guid bookingId, string expectedState, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = factory.Services.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IBookingSagaStateReader>();
            var snapshot = await reader.GetByBookingIdAsync(bookingId, CancellationToken.None);

            if (snapshot is not null && string.Equals(snapshot.CurrentState, expectedState, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private static IBusControl CreatePaymentResponder(BookingApiFactory factory) =>
        Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(new Uri($"rabbitmq://{factory.RabbitMqHostName}:{factory.RabbitMqPort}"), host =>
            {
                host.Username("guest");
                host.Password("guest");
            });

            cfg.ReceiveEndpoint($"booking-saga-payment-responder-{Guid.NewGuid():N}", endpoint =>
            {
                endpoint.Handler<ChargePaymentRequestedV1>(async context =>
                {
                    await context.Publish(new PaymentResultReceivedV1(
                        MessageId: Guid.NewGuid(),
                        CorrelationId: context.Message.CorrelationId,
                        BookingId: context.Message.BookingId,
                        PaymentIntentId: Guid.NewGuid(),
                        Status: "Succeeded",
                        ProviderReference: $"it_{context.Message.BookingId:N}",
                        Error: null,
                        OccurredOnUtc: DateTime.UtcNow));
                });
            });
        });

    private static IBusControl CreateRefundObserverBus(BookingApiFactory factory, RefundObserver observer) =>
        Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(new Uri($"rabbitmq://{factory.RabbitMqHostName}:{factory.RabbitMqPort}"), host =>
            {
                host.Username("guest");
                host.Password("guest");
            });

            cfg.ReceiveEndpoint($"booking-saga-refund-observer-{Guid.NewGuid():N}", endpoint =>
            {
                endpoint.Handler<RefundRequestedV1>(context =>
                {
                    observer.Observe(context.Message.BookingId);
                    return Task.CompletedTask;
                });
            });
        });

    private static async Task<bool> WaitForBookingStatusAsync(HttpClient client, Guid customerId, string expectedStatus, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var bookings = await GetCustomerBookingsAsync(client, customerId);
            if (bookings.Any(item => string.Equals(item.Status, expectedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            await Task.Delay(250);
        }
        return false;
    }

    private static async Task<IReadOnlyCollection<UserBookingItemResponse>> GetCustomerBookingsAsync(HttpClient client, Guid customerId)
    {
        _ = customerId;

        var bookings = await client.GetFromJsonAsync<UserBookingsResponse>("/api/bookings");
        return bookings?.Items ?? Array.Empty<UserBookingItemResponse>();
    }

    private sealed class RefundObserver : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(0);
        private Guid _observedBookingId;

        public void Observe(Guid bookingId)
        {
            _observedBookingId = bookingId;
            _semaphore.Release();
        }

        public async Task<bool> WaitForRefundAsync(Guid expectedBookingId, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                await _semaphore.WaitAsync(cancellation.Token);
                return _observedBookingId == expectedBookingId;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void Dispose() => _semaphore.Dispose();
    }
}
