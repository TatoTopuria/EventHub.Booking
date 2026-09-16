using Booking.Service.Application.Bookings.Commands.ConfirmBooking;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Abstractions.Observability;
using MassTransit;
using MediatR;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class ConfirmBookingRequestedConsumer(
    ISender sender,
    IInboxMessageStore inbox,
    IdempotentConsumerExecutor executor,
    ICorrelationContextAccessor correlationContextAccessor)
    : IConsumer<ConfirmBookingRequestedV1>
{
    public async Task Consume(ConsumeContext<ConfirmBookingRequestedV1> context)
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
                    var result = await sender.Send(
                        new ConfirmBookingCommand(context.Message.BookingId, context.Message.PaidAmount, context.Message.Currency),
                        cancellationToken);

                    if (result.IsFailure && !string.Equals(result.Error, "Only pending bookings can be confirmed.", StringComparison.Ordinal))
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