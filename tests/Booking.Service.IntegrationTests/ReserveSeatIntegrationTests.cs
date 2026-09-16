using System.Net.Http.Json;
using Booking.Service.Api.Contracts;
using Booking.Service.Application.Bookings.Queries.GetUserBookings;
using Booking.Service.Application.Events.Queries.ListEvents;
using FluentAssertions;

namespace Booking.Service.IntegrationTests;

public sealed class ReserveSeatIntegrationTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task ReserveSeatCommand_Should_Reserve_Seat_End_To_End()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var client = factory.CreateClient();

        var eventsResponse = await client.GetFromJsonAsync<ListEventsResponse>("/api/events?pageSize=1");
        eventsResponse.Should().NotBeNull();
        var eventId = eventsResponse!.Items.Single().EventId;

        var customerId = Guid.NewGuid();
        GatewayTestAuthHeaders.AddForUser(client, customerId);
        var reserveRequest = new ReserveSeatRequest(eventId, "A1");

        var reserveResponse = await client.PostAsJsonAsync("/api/bookings/reserve", reserveRequest);
        reserveResponse.EnsureSuccessStatusCode();

        var bookings = await client.GetFromJsonAsync<UserBookingsResponse>("/api/bookings");
        bookings.Should().NotBeNull();
        bookings!.Items.Should().ContainSingle();
        bookings.Items.Single().Seats.Should().Contain("A1");
    }
}
