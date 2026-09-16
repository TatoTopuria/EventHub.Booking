using Booking.Service.Application.Abstractions;
using Booking.Service.Application.Common.Behaviors;
using Booking.Service.Application.Contracts.Persistence;
using FluentAssertions;
using MediatR;
using Moq;

namespace Booking.Service.UnitTests;

public sealed class TransactionBehaviorTests
{
    [Fact]
    public async Task Handle_Should_Use_Transaction_For_Multi_Aggregate_Command()
    {
        var transactionExecutor = new Mock<ITransactionExecutor>();
        transactionExecutor
            .Setup(executor => executor.ExecuteAsync(It.IsAny<Func<CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string>>, CancellationToken>((operation, token) => operation(token));

        var behavior = new TransactionBehavior<MultiAggregateRequest, string>(transactionExecutor.Object);

        var response = await behavior.Handle(new MultiAggregateRequest(), () => Task.FromResult("ok"), CancellationToken.None);

        response.Should().Be("ok");
        transactionExecutor.Verify(
            executor => executor.ExecuteAsync(It.IsAny<Func<CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Not_Use_Transaction_For_Single_Aggregate_Command()
    {
        var transactionExecutor = new Mock<ITransactionExecutor>();
        var behavior = new TransactionBehavior<SingleAggregateRequest, string>(transactionExecutor.Object);

        var response = await behavior.Handle(new SingleAggregateRequest(), () => Task.FromResult("ok"), CancellationToken.None);

        response.Should().Be("ok");
        transactionExecutor.Verify(
            executor => executor.ExecuteAsync(It.IsAny<Func<CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed record MultiAggregateRequest : IRequest<string>, IMultiAggregateCommand;

    private sealed record SingleAggregateRequest : IRequest<string>;
}
