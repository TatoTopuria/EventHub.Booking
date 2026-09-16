using Booking.Service.Application.Bookings.Validation;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.UnitTests.Builders;
using FluentAssertions;

namespace Booking.Service.UnitTests.Validation;

/// <summary>
/// Exercises the Chain of Responsibility composition for booking reservation validation.
/// </summary>
public sealed class BookingValidationChainTests
{
    [Fact]
    public async Task Pipeline_Should_Succeed_When_All_Handlers_Pass()
    {
        var evt = new EventBuilder().Published().Build();
        var pipeline = BuildPipeline(blockedCustomerId: null);
        var ctx = new BookingValidationContext(evt, CustomerId.New(), evt.Seats.First());

        var result = await pipeline.ValidateAsync(ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_Should_Short_Circuit_On_Unpublished_Event()
    {
        var evt = new EventBuilder().Build(); // status = Draft
        var pipeline = BuildPipeline(blockedCustomerId: null);
        var ctx = new BookingValidationContext(evt, CustomerId.New(), evt.Seats.First());

        var result = await pipeline.ValidateAsync(ctx, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not accepting bookings");
    }

    [Fact]
    public async Task Pipeline_Should_Short_Circuit_On_Unknown_Seat()
    {
        var evt = new EventBuilder().Published().WithSeats("A1", "A2").Build();
        var pipeline = BuildPipeline(blockedCustomerId: null);
        var ctx = new BookingValidationContext(evt, CustomerId.New(), SeatNumber.Create("Z99").Value);

        var result = await pipeline.ValidateAsync(ctx, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public async Task Pipeline_Should_Short_Circuit_When_Seat_Already_Reserved()
    {
        var evt = new EventBuilder().Published().Build();
        var firstSeat = evt.Seats.First();
        evt.MarkSeatReserved(firstSeat); // consume the seat directly so the chain sees a reserved-state.

        var pipeline = BuildPipeline(blockedCustomerId: null);
        var ctx = new BookingValidationContext(evt, CustomerId.New(), firstSeat);

        var result = await pipeline.ValidateAsync(ctx, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already reserved");
    }

    [Fact]
    public async Task Pipeline_Should_Short_Circuit_When_Customer_Is_Blocked()
    {
        var evt = new EventBuilder().Published().Build();
        var customerId = CustomerId.New();

        var pipeline = BuildPipeline(blockedCustomerId: customerId.Value);
        var ctx = new BookingValidationContext(evt, customerId, evt.Seats.First());

        var result = await pipeline.ValidateAsync(ctx, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("currently blocked");
    }

    [Fact]
    public async Task Pipeline_Should_Run_Handlers_In_Declared_Order_Stopping_At_First_Failure()
    {
        // Draft event + blocked customer + nonexistent seat — the order is
        // EventPublished → SeatExists → SeatAvailable → CustomerNotBlocked, so the failure should
        // surface from the very first handler.
        var evt = new EventBuilder().Build();
        var customerId = CustomerId.New();

        var pipeline = BuildPipeline(blockedCustomerId: customerId.Value);
        var ctx = new BookingValidationContext(evt, customerId, SeatNumber.Create("Z99").Value);

        var result = await pipeline.ValidateAsync(ctx, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not accepting bookings");
    }

    private static BookingValidationPipeline BuildPipeline(Guid? blockedCustomerId)
    {
        var blocklist = new InMemoryBlocklist(blockedCustomerId);
        return new BookingValidationPipeline(
            new EventPublishedHandler(),
            new SeatExistsHandler(),
            new SeatAvailableHandler(),
            new CustomerNotBlockedHandler(blocklist));
    }

    private sealed class InMemoryBlocklist : ICustomerBlocklist
    {
        private readonly Guid? _blocked;
        public InMemoryBlocklist(Guid? blocked) => _blocked = blocked;
        public Task<bool> IsBlockedAsync(CustomerId customerId, CancellationToken cancellationToken) =>
            Task.FromResult(_blocked == customerId.Value);
    }
}
