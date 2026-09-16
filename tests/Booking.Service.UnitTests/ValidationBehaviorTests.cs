using Booking.Service.Application.Common.Behaviors;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Booking.Service.UnitTests;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_Should_Throw_When_Validation_Fails()
    {
        var validators = new[] { new FailingValidator() };
        var behavior = new ValidationBehavior<TestRequest, string>(validators);

        var action = async () => await behavior.Handle(new TestRequest(string.Empty), () => Task.FromResult("ok"), CancellationToken.None);

        await action.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_Should_Continue_When_Validation_Succeeds()
    {
        var validators = new[] { new PassingValidator() };
        var behavior = new ValidationBehavior<TestRequest, string>(validators);

        var response = await behavior.Handle(new TestRequest("value"), () => Task.FromResult("ok"), CancellationToken.None);

        response.Should().Be("ok");
    }

    private sealed record TestRequest(string Value) : IRequest<string>;

    private sealed class FailingValidator : AbstractValidator<TestRequest>
    {
        public FailingValidator()
        {
            RuleFor(request => request.Value).NotEmpty();
        }
    }

    private sealed class PassingValidator : AbstractValidator<TestRequest>
    {
        public PassingValidator()
        {
            RuleFor(request => request.Value).NotEmpty();
        }
    }
}
