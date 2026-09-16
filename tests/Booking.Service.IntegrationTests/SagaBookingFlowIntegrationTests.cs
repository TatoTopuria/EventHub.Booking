using System.Net.Http.Json;
using Booking.Service.Api.Contracts;
using Booking.Service.Application.Bookings.Queries.GetUserBookings;
using Booking.Service.Application.Events.Queries.ListEvents;
using BuildingBlocks.Abstractions.Messaging;
using FluentAssertions;
using MassTransit;

namespace Booking.Service.IntegrationTests;

public sealed class SagaBookingFlowIntegrationTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task ReserveSeat_Should_Confirm_Booking_When_Payment_Succeeds()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var responderBus = CreatePaymentResponder(factory, shouldFail: false);
        await responderBus.StartAsync();

        using var client = factory.CreateClient();
        var customerId = Guid.NewGuid();
        GatewayTestAuthHeaders.AddForUser(client, customerId);

        var eventsResponse = await client.GetFromJsonAsync<ListEventsResponse>("/api/events?pageSize=1");
        eventsResponse.Should().NotBeNull();
        var eventId = eventsResponse!.Items.Single().EventId;

        var reserveResponse = await client.PostAsJsonAsync(
            "/api/bookings/reserve",
            new ReserveSeatRequest(eventId, "A1"));
        reserveResponse.EnsureSuccessStatusCode();

        var confirmed = await WaitForBookingStatusAsync(client, customerId, "Confirmed", TimeSpan.FromSeconds(20));
        confirmed.Should().BeTrue("payment success should drive saga into booking confirmation");

        await responderBus.StopAsync();
    }

    [Fact]
    public async Task ReserveSeat_Should_Cancel_Booking_When_Payment_Fails()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var receivedChargeRequest = false;
        var publishedPaymentResult = false;

        var responderBus = Bus.Factory.CreateUsingRabbitMq(cfg =>
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
                    receivedChargeRequest = true;
                    await Task.Delay(500);
                    await context.Publish(new PaymentResultReceivedV1(
                        MessageId: Guid.NewGuid(),
                        CorrelationId: context.Message.CorrelationId,
                        BookingId: context.Message.BookingId,
                        PaymentIntentId: Guid.NewGuid(),
                        Status: "Failed",
                        ProviderReference: $"it_{context.Message.BookingId:N}",
                        Error: "Integration test simulated payment failure.",
                        OccurredOnUtc: DateTime.UtcNow));
                    publishedPaymentResult = true;
                });
            });
        });
        await responderBus.StartAsync();

        using var client = factory.CreateClient();
        var customerId = Guid.NewGuid();
        GatewayTestAuthHeaders.AddForUser(client, customerId);

        var eventsResponse = await client.GetFromJsonAsync<ListEventsResponse>("/api/events?pageSize=1");
        eventsResponse.Should().NotBeNull();
        var eventId = eventsResponse!.Items.Single().EventId;

        var reserveResponse = await client.PostAsJsonAsync(
            "/api/bookings/reserve",
            new ReserveSeatRequest(eventId, "A2"));
        reserveResponse.EnsureSuccessStatusCode();

        var (cancelled, lastStatus, bookingCount) = await WaitForBookingStatusDetailsAsync(client, customerId, "Cancelled", TimeSpan.FromSeconds(20));
        cancelled.Should().BeTrue($"receivedChargeRequest={receivedChargeRequest}, publishedPaymentResult={publishedPaymentResult}, bookingCount={bookingCount}, lastStatus={lastStatus}");

        await responderBus.StopAsync();
    }

    private static IBusControl CreatePaymentResponder(BookingApiFactory factory, bool shouldFail)
    {
        return Bus.Factory.CreateUsingRabbitMq(cfg =>
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
                    var status = shouldFail ? "Failed" : "Succeeded";
                    var error = shouldFail ? "Integration test simulated payment failure." : null;

                    await Task.Delay(500);
                    await context.Publish(new PaymentResultReceivedV1(
                        MessageId: Guid.NewGuid(),
                        CorrelationId: context.Message.CorrelationId,
                        BookingId: context.Message.BookingId,
                        PaymentIntentId: Guid.NewGuid(),
                        Status: status,
                        ProviderReference: $"it_{context.Message.BookingId:N}",
                        Error: error,
                        OccurredOnUtc: DateTime.UtcNow));
                });
            });
        });
    }

    private static async Task<bool> WaitForBookingStatusAsync(HttpClient client, Guid customerId, string expectedStatus, TimeSpan timeout)
    {
        var (matched, _, _) = await WaitForBookingStatusDetailsAsync(client, customerId, expectedStatus, timeout);
        return matched;
    }

    private static async Task<(bool Matched, string? LastStatus, int Count)> WaitForBookingStatusDetailsAsync(HttpClient client, Guid customerId, string expectedStatus, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        string? lastStatus = null;
        var count = 0;

        while (DateTime.UtcNow - startedAt < timeout)
        {
            var bookings = await client.GetFromJsonAsync<UserBookingsResponse>("/api/bookings");
            count = bookings?.Items.Count ?? 0;
            var item = bookings?.Items.FirstOrDefault();
            if (item is not null)
            {
                lastStatus = item.Status;
                if (string.Equals(item.Status, expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, lastStatus, count);
                }
            }

            await Task.Delay(250);
        }

        return (false, lastStatus, count);
    }
}