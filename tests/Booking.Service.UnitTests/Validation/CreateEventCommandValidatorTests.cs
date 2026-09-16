using Booking.Service.Application.Events.Commands.CreateEvent;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Booking.Service.UnitTests.Validation;

public sealed class CreateEventCommandValidatorTests
{
    private readonly CreateEventCommandValidator _validator = new();

    [Fact]
    public void Should_Pass_For_A_Well_Formed_Request()
    {
        var result = _validator.TestValidate(BuildValidCommand());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_Start_Is_After_End()
    {
        var now = DateTime.UtcNow;
        var command = BuildValidCommand() with
        {
            StartsAtUtc = now.AddHours(2),
            EndsAtUtc = now.AddHours(1)
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.StartsAtUtc);
    }

    [Fact]
    public void Should_Fail_When_Start_Equals_End()
    {
        var now = DateTime.UtcNow;
        var command = BuildValidCommand() with
        {
            StartsAtUtc = now,
            EndsAtUtc = now
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.StartsAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Should_Fail_When_Organizer_Is_Blank(string? organizer)
    {
        var command = BuildValidCommand() with { Organizer = organizer! };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Organizer);
    }

    [Fact]
    public void Should_Fail_When_Organizer_Exceeds_Length_Limit()
    {
        var command = BuildValidCommand() with { Organizer = new string('a', 201) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Organizer);
    }

    [Fact]
    public void Should_Fail_When_Seats_Are_Empty()
    {
        var command = BuildValidCommand() with { Seats = Array.Empty<string>() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Seats);
    }

    [Fact]
    public void Should_Fail_When_Any_Seat_Is_Blank()
    {
        var command = BuildValidCommand() with { Seats = new[] { "A1", "", "A3" } };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Seats);
    }

    [Fact]
    public void Should_Fail_When_Seats_Contain_Duplicates_Case_Insensitively()
    {
        var command = BuildValidCommand() with { Seats = new[] { "A1", "a1", "B1" } };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Seats);
    }

    [Fact]
    public void Should_Fail_When_Seat_Count_Exceeds_Hard_Cap()
    {
        var seats = Enumerable.Range(0, 1001).Select(i => $"S{i}").ToArray();
        var command = BuildValidCommand() with { Seats = seats };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Seats);
    }

    [Fact]
    public void Should_Fail_When_Any_Seat_Exceeds_Length_Limit()
    {
        var command = BuildValidCommand() with { Seats = new[] { "A1", new string('S', 21) } };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Seats);
    }

    private static CreateEventCommand BuildValidCommand()
    {
        var startsAt = DateTime.UtcNow.AddDays(7);
        return new CreateEventCommand(
            StartsAtUtc: startsAt,
            EndsAtUtc: startsAt.AddHours(2),
            Organizer: "EventHub Demo",
            Seats: new[] { "A1", "A2", "A3" });
    }
}
