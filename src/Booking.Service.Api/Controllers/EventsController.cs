using Booking.Service.Api.Contracts;
using Booking.Service.Application.Events.Commands.CreateEvent;
using Booking.Service.Application.Events.Commands.UpdateEvent;
using Booking.Service.Application.Events.Queries.GetEventById;
using Booking.Service.Application.Events.Queries.ListEvents;
using BuildingBlocks.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Service.Api.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EventsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Creates a new event. Restricted to organizers and admins.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = EventHubPolicies.OrganizerOrAbove)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateEventCommand(request.StartsAtUtc, request.EndsAtUtc, request.Organizer, request.Seats),
            cancellationToken);

        return result.IsFailure
            ? BadRequest(new { error = result.Error })
            : CreatedAtAction("GetById", new { eventId = result.Value }, new { eventId = result.Value });
    }

    /// <summary>
    /// Patches schedule and/or organizer on an existing event. Restricted to organizers and admins.
    /// </summary>
    /// <remarks>
    /// The aggregate decides what counts as a real change — calling this with the same fields
    /// already on the record is a no-op and emits no integration event, so retries are safe and
    /// downstream caches are not invalidated for nothing.
    /// </remarks>
    [HttpPatch("{eventId:guid}")]
    [Authorize(Policy = EventHubPolicies.OrganizerOrAbove)]
    public async Task<IActionResult> UpdateAsync(
        Guid eventId,
        [FromBody] UpdateEventRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateEventCommand(eventId, request.StartsAtUtc, request.EndsAtUtc, request.Organizer),
            cancellationToken);

        return result.IsFailure
            ? BadRequest(new { error = result.Error })
            : NoContent();
    }

    /// <summary>
    /// Gets an event by id.
    /// </summary>
    [HttpGet("{eventId:guid}")]
    [ActionName("GetById")]
    public async Task<IActionResult> GetByIdAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEventByIdQuery(eventId), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Lists events with keyset pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateTime? lastStartsAtUtc,
        [FromQuery] Guid? lastEventId,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new ListEventsQuery(lastStartsAtUtc, lastEventId, pageSize), cancellationToken);
        return Ok(result);
    }
}
