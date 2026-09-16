using System.Security.Cryptography;
using System.Text;
using FluentAssertions;

namespace Booking.Service.IntegrationTests;

public sealed class CatalogGrpcFailureIntegrationTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task GetEventDetails_WhenCatalogIsDown_ShouldReturn503ProblemDetails()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        using var client = factory.CreateClient();

        var userId = Guid.NewGuid().ToString();
        var roles = string.Empty;
        var signature = CreateSignature($"{userId}|{roles}", "INSERT_DEVELOPMENT_SECRET_HERE_CHANGE_ME");

        client.DefaultRequestHeaders.Add("X-Forwarded-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Forwarded-Roles", roles);
        client.DefaultRequestHeaders.Add("X-Gateway-Signature", signature);

        var response = await client.GetAsync($"/api/bookings/events/{Guid.NewGuid()}/details");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Catalog unavailable");
        payload.Should().NotContain("RpcException");
    }

    private static string CreateSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
    }
}
