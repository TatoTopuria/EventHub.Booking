using System.Net;
using System.Net.Http.Json;
using Booking.Service.Api.Contracts;
using FluentAssertions;

namespace Booking.Service.IntegrationTests;

public sealed class MiddlewareOrderingIntegrationTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task Exception_Response_Should_Contain_Correlation_Id()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        var client = factory.CreateClient();
        GatewayTestAuthHeaders.AddForUser(client, Guid.NewGuid(), BuildingBlocks.Security.EventHubRoles.Customer);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "it-test-correlation");

        var response = await client.PostAsJsonAsync("/api/bookings/reserve", new ReserveSeatRequest(Guid.Empty, string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values.Should().ContainSingle(value => value == "it-test-correlation");

        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.CorrelationId.Should().Be("it-test-correlation");
    }

    private sealed record ErrorResponse(string Title, int Status, string Detail, string? CorrelationId);
}
