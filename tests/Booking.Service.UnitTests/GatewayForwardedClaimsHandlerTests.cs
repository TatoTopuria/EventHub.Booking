using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BuildingBlocks.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Booking.Service.UnitTests;

/// <summary>
/// Regression tests for the gateway-forwarded claims authentication handler. These exist to keep the
/// HMAC + role-parsing contract honest after the handler was extracted to BuildingBlocks and shared
/// across Booking, Payment, and Analytics services.
/// </summary>
public sealed class GatewayForwardedClaimsHandlerTests
{
    private const string SharedSecret = "unit-test-shared-secret";

    [Fact]
    public async Task HandleAuthenticate_Should_Materialize_Principal_With_Single_Role()
    {
        var userId = Guid.NewGuid().ToString("D");
        const string roles = EventHubRoles.Customer;

        var result = await AuthenticateAsync(userId, roles, SignPayload(userId, roles));

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(userId);
        result.Principal.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Should().BeEquivalentTo(new[] { EventHubRoles.Customer });
    }

    [Fact]
    public async Task HandleAuthenticate_Should_Split_Multiple_Roles_On_Comma()
    {
        var userId = Guid.NewGuid().ToString("D");
        var roles = $"{EventHubRoles.Organizer},{EventHubRoles.Admin}";

        var result = await AuthenticateAsync(userId, roles, SignPayload(userId, roles));

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Should().BeEquivalentTo(new[] { EventHubRoles.Organizer, EventHubRoles.Admin });
    }

    [Fact]
    public async Task HandleAuthenticate_Should_Trim_Whitespace_Around_Each_Role()
    {
        var userId = Guid.NewGuid().ToString("D");
        var rolesAsTransmitted = " Organizer ,  Admin ";

        // The HMAC must be over the raw header value, not a trimmed copy.
        var result = await AuthenticateAsync(userId, rolesAsTransmitted, SignPayload(userId, rolesAsTransmitted));

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Should().BeEquivalentTo(new[] { EventHubRoles.Organizer, EventHubRoles.Admin });
    }

    [Fact]
    public async Task HandleAuthenticate_Should_Fail_When_Signature_Missing()
    {
        var userId = Guid.NewGuid().ToString("D");

        var result = await AuthenticateAsync(userId, EventHubRoles.Customer, signature: null);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Missing gateway signature");
    }

    [Fact]
    public async Task HandleAuthenticate_Should_Fail_When_Signature_Wrong()
    {
        var userId = Guid.NewGuid().ToString("D");
        var tamperedSignature = SignPayload(userId, "Admin");

        var result = await AuthenticateAsync(userId, EventHubRoles.Customer, tamperedSignature);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Invalid gateway signature");
    }

    [Fact]
    public async Task HandleAuthenticate_Should_Fail_When_UserId_Missing()
    {
        var result = await AuthenticateAsync(userId: null, EventHubRoles.Customer, "not-going-to-be-checked");

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("forwarded user identifier");
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(string? userId, string roles, string? signature)
    {
        var optionsMonitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Setup(monitor => monitor.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var gatewayOptions = Options.Create(new GatewayAuthOptions { SharedSecret = SharedSecret });

        var handler = new GatewayForwardedClaimsHandler(
            optionsMonitor.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            gatewayOptions);

        var scheme = new AuthenticationScheme(
            GatewayForwardedClaimsHandler.SchemeName,
            GatewayForwardedClaimsHandler.SchemeName,
            typeof(GatewayForwardedClaimsHandler));

        var httpContext = new DefaultHttpContext();
        if (userId is not null)
        {
            httpContext.Request.Headers["X-Forwarded-UserId"] = userId;
        }
        httpContext.Request.Headers["X-Forwarded-Roles"] = roles;
        if (signature is not null)
        {
            httpContext.Request.Headers["X-Gateway-Signature"] = signature;
        }

        await handler.InitializeAsync(scheme, httpContext);
        return await handler.AuthenticateAsync();
    }

    private static string SignPayload(string userId, string roles)
    {
        var payload = string.Join('|', userId, roles);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SharedSecret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}
