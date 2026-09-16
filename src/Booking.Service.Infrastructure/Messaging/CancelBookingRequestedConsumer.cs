using Booking.Service.Application.Bookings.Commands.CancelBooking;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Abstractions.Observability;
using MassTransit;
using MediatR;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class CancelBookingRequestedConsumer(
    ISender sender,
    IInboxMessageStore inbox,
    IdempotentConsumerExecutor executor,
    ICorrelationContextAccessor correlationContextAccessor)
    : IConsumer<CancelBookingRequestedV1>
{
    public async Task Consume(ConsumeContext<CancelBookingRequestedV1> context)
    {
        var messageId = context.Message.MessageId.ToString("N");
        correlationContextAccessor.CorrelationId = context.Message.CorrelationId;

        try
        {
            await executor.ExecuteAsync(
                messageId,
                inbox,
                async cancellationToken =>
                {
                    var result = await sender.Send(new CancelBookingCommand(context.Message.BookingId), cancellationToken);

                    if (result.IsFailure && !string.Equals(result.Error, "Booking cannot be cancelled from the current state.", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(result.Error);
                    }
                },
                context.CancellationToken);
        }
        finally
        {
            correlationContextAccessor.CorrelationId = null;
        }
    }
}