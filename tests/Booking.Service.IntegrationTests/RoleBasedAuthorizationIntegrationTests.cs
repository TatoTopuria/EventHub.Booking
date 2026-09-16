using System.Net;
using System.Net.Http.Json;
using Booking.Service.Api.Contracts;
using Booking.Service.Application.Bookings.Queries.GetUserBookings;
using Booking.Service.Application.Events.Queries.ListEvents;
using BuildingBlocks.Security;
using FluentAssertions;

namespace Booking.Service.IntegrationTests;

/// <summary>
/// Verifies F3 — POST /api/events is gated by <see cref="EventHubPolicies.OrganizerOrAbove"/> —
/// and F4 — Confirm/Cancel endpoints on /api/bookings are gated by
/// <see cref="EventHubPolicies.BookingOwnerOrAdmin"/>.
/// </summary>
public sealed class RoleBasedAuthorizationIntegrationTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task CreateEvent_Should_Return_403_When_Caller_Is_Customer()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        using var client = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(client, Guid.NewGuid(), EventHubRoles.Customer);

        var response = await client.PostAsJsonAsync("/api/events", BuildValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateEvent_Should_Return_403_When_Caller_Has_No_Role()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        using var client = factory.CreateClient();
        // Signed envelope but with an empty role list — the policy must reject.
        GatewayTestAuthHeaders.AddForUser(client, Guid.NewGuid(), roles: string.Empty);

        var response = await client.PostAsJsonAsync("/api/events", BuildValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateEvent_Should_Return_201_When_Caller_Is_Organizer()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        using var client = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(client, Guid.NewGuid(), EventHubRoles.Organizer);

        var response = await client.PostAsJsonAsync("/api/events", BuildValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateEvent_Should_Return_201_When_Caller_Is_Admin()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        using var client = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(client, Guid.NewGuid(), EventHubRoles.Admin);

        var response = await client.PostAsJsonAsync("/api/events", BuildValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateEvent_Should_Return_401_When_Gateway_Signature_Missing()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        using var client = factory.CreateClient();
        // No GatewayTestAuthHeaders call — request goes through unauthenticated.

        var response = await client.PostAsJsonAsync("/api/events", BuildValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static CreateEventRequest BuildValidRequest()
    {
        var startsAtUtc = DateTime.UtcNow.AddDays(30);
        return new CreateEventRequest(
            StartsAtUtc: startsAtUtc,
            EndsAtUtc: startsAtUtc.AddHours(2),
            Organizer: "Integration Test Organizer",
            Seats: new[] { "A1", "A2", "A3" });
    }

    // ── F4: BookingOwnerOrAdmin policy on Confirm / Cancel ─────────────────────────────────────

    [Fact]
    public async Task ConfirmBooking_Should_Return_403_When_Caller_Is_Not_Owner()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var bookingId = await ReserveAsAsync(ownerCustomerId: Guid.NewGuid(), seatNumber: "F4-A1");

        using var attacker = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(attacker, Guid.NewGuid(), EventHubRoles.Customer);

        var response = await attacker.PostAsJsonAsync(
            $"/api/bookings/{bookingId:D}/confirm",
            new ConfirmBookingRequest(100m, "USD"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a different Customer must not be able to confirm someone else's booking");
    }

    [Fact]
    public async Task CancelBooking_Should_Return_403_When_Caller_Is_Not_Owner()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var bookingId = await ReserveAsAsync(ownerCustomerId: Guid.NewGuid(), seatNumber: "F4-A2");

        using var attacker = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(attacker, Guid.NewGuid(), EventHubRoles.Customer);

        var response = await attacker.PostAsync($"/api/bookings/{bookingId:D}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfirmBooking_Should_Succeed_When_Caller_Is_Owner()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var ownerId = Guid.NewGuid();
        var bookingId = await ReserveAsAsync(ownerId, seatNumber: "F4-A3");

        using var owner = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(owner, ownerId, EventHubRoles.Customer);

        var response = await owner.PostAsJsonAsync(
            $"/api/bookings/{bookingId:D}/confirm",
            new ConfirmBookingRequest(100m, "USD"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CancelBooking_Should_Succeed_When_Caller_Is_Admin_For_Someone_Elses_Booking()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        // Admin override is the operational backstop — must work even when the admin is not the
        // owner. Otherwise ops cannot clean up stale bookings without forging a customer JWT.
        var bookingId = await ReserveAsAsync(ownerCustomerId: Guid.NewGuid(), seatNumber: "F4-B1");

        using var admin = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(admin, Guid.NewGuid(), EventHubRoles.Admin);

        var response = await admin.PostAsync($"/api/bookings/{bookingId:D}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetBookings_Should_Ignore_RequestedCustomerId_When_Caller_Is_Customer()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var ownerCustomerId = Guid.NewGuid();
        await ReserveAsAsync(ownerCustomerId, seatNumber: "F4-B2");

        using var attacker = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(attacker, Guid.NewGuid(), EventHubRoles.Customer);

        var response = await attacker.GetFromJsonAsync<UserBookingsResponse>($"/api/bookings?customerId={ownerCustomerId:D}");

        response.Should().NotBeNull();
        response!.Items.Should().BeEmpty("customer-scoped listing must ignore a requested foreign customer id");
    }

    [Fact]
    public async Task GetBookings_Should_Return_RequestedCustomerId_When_Caller_Is_Admin()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var ownerCustomerId = Guid.NewGuid();
        await ReserveAsAsync(ownerCustomerId, seatNumber: "F4-B3");

        using var admin = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(admin, Guid.NewGuid(), EventHubRoles.Admin);

        var response = await admin.GetFromJsonAsync<UserBookingsResponse>($"/api/bookings?customerId={ownerCustomerId:D}");

        response.Should().NotBeNull();
        response!.Items.Should().ContainSingle(item => item.Seats.Contains("F4-B3"));
    }

    [Fact]
    public async Task ConfirmBooking_Should_Return_403_When_BookingId_Does_Not_Exist()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        // 403, not 404 — see BookingOwnerAuthorizationHandler: missing-vs-not-owner are folded
        // together so probing the existence of other users' bookings is impossible.
        using var client = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(client, Guid.NewGuid(), EventHubRoles.Customer);

        var response = await client.PostAsJsonAsync(
            $"/api/bookings/{Guid.NewGuid():D}/confirm",
            new ConfirmBookingRequest(100m, "USD"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Helper: reserves a single seat as the given owner and returns the resulting booking id.
    /// Each call uses a unique seat number to avoid contention with sibling tests in the same
    /// factory-shared event aggregate.
    /// </summary>
    private async Task<Guid> ReserveAsAsync(Guid ownerCustomerId, string seatNumber)
    {
        using var owner = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(owner, ownerCustomerId, EventHubRoles.Customer);

        var events = await owner.GetFromJsonAsync<ListEventsResponse>("/api/events?pageSize=1");
        events.Should().NotBeNull();
        var eventId = events!.Items.Single().EventId;

        var reserveResponse = await owner.PostAsJsonAsync(
            "/api/bookings/reserve",
            new ReserveSeatRequest(eventId, seatNumber));
        reserveResponse.EnsureSuccessStatusCode();

        var payload = await reserveResponse.Content.ReadFromJsonAsync<ReserveSeatResponse>();
        payload.Should().NotBeNull();
        return payload!.BookingId;
    }

    private sealed record ReserveSeatResponse(Guid BookingId);
}
