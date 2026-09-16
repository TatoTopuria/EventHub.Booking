using System.Security.Claims;
using BuildingBlocks.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booking.Service.UnitTests.Security;

/// <summary>
/// Pins every authorization branch of <see cref="BookingOwnerAuthorizationHandler"/>. Each test
/// represents a real attacker / operator scenario, captured at the handler boundary so a
/// regression in the policy logic surfaces here long before any integration test runs.
/// </summary>
public sealed class BookingOwnerAuthorizationHandlerTests
{
    private static readonly Guid BookingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AttackerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Succeeds_For_Booking_Owner()
    {
        var context = BuildContext(OwnerId, role: EventHubRoles.Customer);
        var handler = BuildHandler(
            httpContext: BuildHttpContextWithRoute(BookingId),
            resolver: new StubResolver(OwnerId));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        context.HasFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Succeeds_For_Admin_Even_When_Not_Owner()
    {
        var context = BuildContext(AttackerId, role: EventHubRoles.Admin);
        // Resolver intentionally NOT consulted for admin — short-circuit short-cuts the DB hit.
        var probe = new StubResolver(OwnerId, throwIfCalled: true);
        var handler = BuildHandler(
            httpContext: BuildHttpContextWithRoute(BookingId),
            resolver: probe);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        probe.CallCount.Should().Be(0, "admin path must not pay for an unnecessary booking lookup");
    }

    [Fact]
    public async Task Fails_For_Authenticated_Non_Owner()
    {
        var context = BuildContext(AttackerId, role: EventHubRoles.Customer);
        var handler = BuildHandler(
            httpContext: BuildHttpContextWithRoute(BookingId),
            resolver: new StubResolver(OwnerId));

        await handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_When_Booking_Does_Not_Exist()
    {
        // Pinned behaviour: missing booking returns the same 403 as wrong owner, so the API does
        // not leak the existence of other users' bookings via 403-vs-404 timing or message.
        var context = BuildContext(OwnerId, role: EventHubRoles.Customer);
        var handler = BuildHandler(
            httpContext: BuildHttpContextWithRoute(BookingId),
            resolver: new StubResolver(ownerCustomerId: null));

        await handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_When_BookingId_Route_Value_Missing()
    {
        var context = BuildContext(OwnerId, role: EventHubRoles.Customer);
        var httpContext = new DefaultHttpContext();
        // Route values left empty — no bookingId to authorize against.

        var handler = BuildHandler(
            httpContext: httpContext,
            resolver: new StubResolver(OwnerId, throwIfCalled: true));

        await handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_When_BookingId_Route_Value_Not_A_Guid()
    {
        var context = BuildContext(OwnerId, role: EventHubRoles.Customer);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues = new RouteValueDictionary
        {
            [BookingOwnerAuthorizationHandler.BookingIdRouteValue] = "not-a-guid"
        };

        var handler = BuildHandler(
            httpContext: httpContext,
            resolver: new StubResolver(OwnerId, throwIfCalled: true));

        await handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_When_User_Has_No_NameIdentifier_Claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, EventHubRoles.Customer) },
            authenticationType: "Test"));
        var context = new AuthorizationHandlerContext(
            new[] { new BookingOwnerOrAdminRequirement() },
            principal,
            resource: null);

        var handler = BuildHandler(
            httpContext: BuildHttpContextWithRoute(BookingId),
            resolver: new StubResolver(OwnerId, throwIfCalled: true));

        await handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_When_Resolver_Throws()
    {
        // Resolver fault (DB outage, gRPC channel break) must fail closed, not open.
        var context = BuildContext(OwnerId, role: EventHubRoles.Customer);
        var handler = BuildHandler(
            httpContext: BuildHttpContextWithRoute(BookingId),
            resolver: new ThrowingResolver());

        await handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_When_HttpContext_Missing()
    {
        // No active HttpContext means the handler cannot read route values — and an auth handler
        // outside a request scope makes no sense. Fail closed.
        var context = BuildContext(OwnerId, role: EventHubRoles.Customer);
        var handler = BuildHandler(
            httpContext: null,
            resolver: new StubResolver(OwnerId, throwIfCalled: true));

        await handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    // ─── helpers ───

    private static AuthorizationHandlerContext BuildContext(Guid userId, string role)
    {
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new AuthorizationHandlerContext(
            new[] { new BookingOwnerOrAdminRequirement() },
            new ClaimsPrincipal(identity),
            resource: null);
    }

    private static HttpContext BuildHttpContextWithRoute(Guid bookingId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues = new RouteValueDictionary
        {
            [BookingOwnerAuthorizationHandler.BookingIdRouteValue] = bookingId.ToString("D")
        };
        return httpContext;
    }

    private static BookingOwnerAuthorizationHandler BuildHandler(
        HttpContext? httpContext,
        IBookingOwnershipResolver resolver)
    {
        return new BookingOwnerAuthorizationHandler(
            new StubHttpContextAccessor(httpContext),
            resolver,
            NullLogger<BookingOwnerAuthorizationHandler>.Instance);
    }

    private sealed class StubHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get => httpContext;
            set => httpContext = value;
        }
    }

    private sealed class StubResolver(Guid? ownerCustomerId, bool throwIfCalled = false) : IBookingOwnershipResolver
    {
        public int CallCount { get; private set; }

        public Task<Guid?> GetOwnerCustomerIdAsync(Guid bookingId, CancellationToken cancellationToken)
        {
            CallCount++;
            if (throwIfCalled)
            {
                throw new InvalidOperationException(
                    "Resolver was called unexpectedly — the short-circuit branch should have avoided it.");
            }
            return Task.FromResult(ownerCustomerId);
        }
    }

    private sealed class ThrowingResolver : IBookingOwnershipResolver
    {
        public Task<Guid?> GetOwnerCustomerIdAsync(Guid bookingId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated resolver fault (e.g. DB outage).");
    }
}
