using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Primitives;
using MediatR;

namespace Booking.Service.Application.Events.Commands.UpdateEvent;

/// <summary>
/// Patches the mutable fields of an existing <see cref="Domain.Aggregates.Event"/>. Either field
/// may be omitted (PATCH semantics) — the aggregate decides what counts as a real change and only
/// emits an <c>EventUpdated</c> domain event when something actually moved, so a no-op call is
/// safe and cheap (no spurious cache invalidation downstream).
/// </summary>
/// <remarks>
/// Sized for the realistic Organizer workflow: reschedule an event or hand it off to a co-host.
/// Seat configuration is intentionally NOT mutable here — that would race the reservation lock
/// and the optimistic-concurrency token on <c>event_seats.version</c>; if seat layout edits ever
/// become a requirement they need their own bounded command with a separate invariant story.
/// </remarks>
public sealed record UpdateEventCommand(
    Guid EventId,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    string? Organizer) : IRequest<Result>;

public sealed class UpdateEventCommandHandler(
    IEventRepository eventRepository,
    IBookingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateEventCommand, Result>
{
    public async Task<Result> Handle(UpdateEventCommand request, CancellationToken cancellationToken)
    {
        var eventIdResult = EventId.Create(request.EventId);
        if (eventIdResult.IsFailure)
        {
            return Result.Failure(eventIdResult.Error);
        }

        var eventAggregate = await eventRepository.GetByIdAsync(eventIdResult.Value, cancellationToken);
        if (eventAggregate is null)
        {
            return Result.Failure("Event was not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (request.StartsAtUtc.HasValue || request.EndsAtUtc.HasValue)
        {
            // Partial schedule patches fall back to the unchanged side, so a caller can PATCH just
            // StartsAtUtc without rescheduling the end (and vice versa).
            var startsAt = request.StartsAtUtc ?? eventAggregate.Schedule.StartsAtUtc;
            var endsAt = request.EndsAtUtc ?? eventAggregate.Schedule.EndsAtUtc;

            var scheduleResult = EventSchedule.Create(startsAt, endsAt);
            if (scheduleResult.IsFailure)
            {
                return Result.Failure(scheduleResult.Error);
            }

            var rescheduleResult = eventAggregate.Reschedule(scheduleResult.Value, now);
            if (rescheduleResult.IsFailure)
            {
                return rescheduleResult;
            }
        }

        if (request.Organizer is not null)
        {
            var organizerResult = eventAggregate.ChangeOrganizer(request.Organizer, now);
            if (organizerResult.IsFailure)
            {
                return organizerResult;
            }
        }

        await eventRepository.UpdateAsync(eventAggregate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
