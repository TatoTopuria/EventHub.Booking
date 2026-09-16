using Booking.Service.Application.Abstractions;
using Booking.Service.Application.Contracts.Persistence;
using MediatR;

namespace Booking.Service.Application.Common.Behaviors;

public sealed class TransactionBehavior<TRequest, TResponse>(ITransactionExecutor transactionExecutor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IMultiAggregateCommand)
        {
            return await next();
        }

        return await transactionExecutor.ExecuteAsync(_ => next(), cancellationToken);
    }
}
