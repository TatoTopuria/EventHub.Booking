using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Security;

namespace Booking.Service.IntegrationTests;

/// <summary>
/// Mints the X-Forwarded-* + X-Gateway-Signature headers that the real ApiGateway emits, so integration
/// tests can hit Booking.Service directly without going through YARP.
/// </summary>
/// <remarks>
/// Defaults to the <see cref="EventHubRoles.Customer"/> role so that role-sensitive policies behave
/// the same in tests as in production. Pass a different role (or comma-separated list) to test role gates.
/// </remarks>
internal static class GatewayTestAuthHeaders
{
    private const string SharedSecret = "INSERT_DEVELOPMENT_SECRET_HERE_CHANGE_ME";

    public static void AddForUser(HttpClient client, Guid userId, string roles = EventHubRoles.Customer)
    {
        var payload = $"{userId:D}|{roles}";
        var signature = CreateSignature(payload, SharedSecret);

        client.DefaultRequestHeaders.Remove("X-Forwarded-UserId");
        client.DefaultRequestHeaders.Remove("X-Forwarded-Roles");
        client.DefaultRequestHeaders.Remove("X-Gateway-Signature");

        client.DefaultRequestHeaders.Add("X-Forwarded-UserId", userId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Forwarded-Roles", roles);
        client.DefaultRequestHeaders.Add("X-Gateway-Signature", signature);
    }

    private static string CreateSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
    }
}