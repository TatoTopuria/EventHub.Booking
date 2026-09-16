using Booking.Service.Application.Common.Behaviors;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Booking.Service.UnitTests;

public sealed class LoggingBehaviorTests
{
    [Fact]
    public async Task Handle_Should_Log_Start_And_End()
    {
        var logger = new Mock<ILogger<LoggingBehavior<TestRequest, string>>>();
        var behavior = new LoggingBehavior<TestRequest, string>(logger.Object);
        var request = new TestRequest("hello");

        var response = await behavior.Handle(request, () => Task.FromResult("ok"), CancellationToken.None);

        response.Should().Be("ok");
        logger.VerifyLog(LogLevel.Information, "Handling", Times.Once());
        logger.VerifyLog(LogLevel.Information, "Handled", Times.Once());
    }

    public sealed record TestRequest(string Value) : IRequest<string>;
}

internal static class LoggerMockExtensions
{
    public static void VerifyLog<T>(this Mock<ILogger<T>> logger, LogLevel level, string containsMessage, Times times)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains(containsMessage, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
