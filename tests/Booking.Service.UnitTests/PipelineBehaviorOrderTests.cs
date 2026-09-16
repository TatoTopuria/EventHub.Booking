using Booking.Service.Application;
using Booking.Service.Application.Common.Behaviors;
using Booking.Service.Application.Contracts.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Service.UnitTests;

public sealed class PipelineBehaviorOrderTests
{
    [Fact]
    public void AddApplication_Should_Register_Pipeline_Behaviors_In_Required_Order()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITransactionExecutor, NoOpTransactionExecutor>();

        services.AddApplication();

        using var serviceProvider = services.BuildServiceProvider();
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<FakeCommand, string>>().Select(behavior => behavior.GetType()).ToArray();

        behaviors.Should().ContainInOrder(
            typeof(LoggingBehavior<FakeCommand, string>),
            typeof(ValidationBehavior<FakeCommand, string>),
            typeof(TransactionBehavior<FakeCommand, string>));
    }

    private sealed record FakeCommand : IRequest<string>;

    private sealed class NoOpTransactionExecutor : ITransactionExecutor
    {
        public Task<TResponse> ExecuteAsync<TResponse>(Func<CancellationToken, Task<TResponse>> operation, CancellationToken cancellationToken = default)
        {
            return operation(cancellationToken);
        }
    }
}
