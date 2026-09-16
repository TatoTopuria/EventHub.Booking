using Booking.Service.Api.Contracts;
using Booking.Service.Application.Bookings.Commands.CancelBooking;
using Booking.Service.Api.Services;
using Booking.Service.Application.Bookings.Commands.ConfirmBooking;
using Booking.Service.Application.Bookings.Commands.ReserveSeat;
using Booking.Service.Application.Bookings.Queries.GetBookingSagaState;
using Booking.Service.Application.Bookings.Queries.GetUserBookings;
using BuildingBlocks.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Booking.Service.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public sealed class BookingsController(ISender sender, ICatalogEventDetailsClient catalogEventDetailsClient) : ControllerBase
{
    /// <summary>
    /// Reserves a seat for an event.
    /// </summary>
    /// <remarks>
    /// Plain <c>[Authorize]</c> only — this endpoint CREATES the booking, so there is no existing
    /// row to check ownership against. The created booking is then authorized via the resource-
    /// based <see cref="EventHubPolicies.BookingOwnerOrAdmin"/> policy on Confirm / Cancel below.
    /// The effective customer is always derived from the authenticated principal; callers cannot
    /// reserve on behalf of another user by supplying a different customer id in the payload.
    /// </remarks>
    [HttpPost("reserve")]
    public async Task<IActionResult> ReserveSeatAsync([FromBody] ReserveSeatRequest request, CancellationToken cancellationToken)
    {
        var customerId = ResolveAuthenticatedCustomerId();
        if (customerId is null)
        {
            return Unauthorized(new { error = "Authenticated customer identifier is missing or invalid." });
        }

        var result = await sender.Send(new ReserveSeatCommand(request.EventId, customerId.Value, request.SeatNumber), cancellationToken);

        return result.IsFailure
            ? BadRequest(new { error = result.Error })
            : Ok(new { bookingId = result.Value });
    }

    /// <summary>
    /// Confirms an existing booking. Gated by <see cref="EventHubPolicies.BookingOwnerOrAdmin"/>
    /// so only the booking's owning customer (or an Admin) can flip it to <c>Confirmed</c>.
    /// </summary>
    [HttpPost("{bookingId:guid}/confirm")]
    [Authorize(Policy = EventHubPolicies.BookingOwnerOrAdmin)]
    public async Task<IActionResult> ConfirmBookingAsync(
        Guid bookingId,
        [FromBody] ConfirmBookingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ConfirmBookingCommand(bookingId, request.Amount, request.Currency), cancellationToken);
        return result.IsFailure ? BadRequest(new { error = result.Error }) : NoContent();
    }

    /// <summary>
    /// Cancels an existing booking and releases its seats. Gated by
    /// <see cref="EventHubPolicies.BookingOwnerOrAdmin"/> — same ownership rule as Confirm.
    /// </summary>
    [HttpPost("{bookingId:guid}/cancel")]
    [Authorize(Policy = EventHubPolicies.BookingOwnerOrAdmin)]
    public async Task<IActionResult> CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelBookingCommand(bookingId), cancellationToken);
        return result.IsFailure ? BadRequest(new { error = result.Error }) : NoContent();
    }

    /// <summary>
    /// Lists bookings for the authenticated customer with keyset pagination.
    /// Admin callers may optionally supply <c>customerId</c> to inspect another customer's bookings.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUserBookingsAsync(
        [FromQuery] Guid? customerId,
        [FromQuery] DateTime? lastCreatedAtUtc,
        [FromQuery] Guid? lastBookingId,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var effectiveCustomerId = ResolveCustomerIdForList(customerId);
        if (effectiveCustomerId is null)
        {
            return Unauthorized(new { error = "Authenticated customer identifier is missing or invalid." });
        }

        var result = await sender.Send(
            new GetUserBookingsQuery(effectiveCustomerId.Value, lastCreatedAtUtc, lastBookingId, pageSize),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns the hosting replica identifier for load-balancing validation.
    /// </summary>
    [HttpGet("replica")]
    public IActionResult GetReplicaAsync()
    {
        return Ok(new { host = Environment.MachineName });
    }

    /// <summary>
    /// Gets event details from Catalog through gRPC.
    /// </summary>
    [HttpGet("events/{eventId:guid}/details")]
    public async Task<IActionResult> GetEventDetailsFromCatalogAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var result = await catalogEventDetailsClient.GetEventDetailsAsync(eventId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// F5 ops endpoint — returns the booking-payment saga snapshot for the given booking, or 404
    /// if no saga ever started (e.g. the booking went straight from <c>Confirmed</c> to
    /// <c>Cancelled</c> without payment). Restricted to Admin so customers can't probe internal
    /// orchestration state.
    /// </summary>
    [HttpGet("{bookingId:guid}/saga-state")]
    [Authorize(Policy = EventHubPolicies.AdminOnly)]
    public async Task<IActionResult> GetSagaStateAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var snapshot = await sender.Send(new GetBookingSagaStateQuery(bookingId), cancellationToken);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    private Guid? ResolveCustomerIdForList(Guid? requestedCustomerId)
    {
        if (User.IsInRole(EventHubRoles.Admin) && requestedCustomerId.HasValue)
        {
            return requestedCustomerId.Value;
        }

        return ResolveAuthenticatedCustomerId();
    }

    private Guid? ResolveAuthenticatedCustomerId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(rawUserId, out var userId) ? userId : null;
    }
}
