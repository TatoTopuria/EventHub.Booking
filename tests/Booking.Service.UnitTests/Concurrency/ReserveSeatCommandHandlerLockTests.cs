using Booking.Service.Application.Bookings.Commands.ReserveSeat;
using Booking.Service.Application.Bookings.Concurrency;
using Booking.Service.Application.Bookings.Validation;
using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.ValueObjects;
using FluentAssertions;
using Moq;

namespace Booking.Service.UnitTests.Concurrency;

/// <summary>
/// Verifies that when <see cref="ISeatReservationLock"/> returns null the handler short-circuits
/// before touching any repository — no orphaned bookings, no event load, no outbox flush.
/// </summary>
public sealed class ReserveSeatCommandHandlerLockTests
{
    [Fact]
    public async Task Handle_Should_Return_Failure_And_Touch_Nothing_When_Lock_Cannot_Be_Acquired()
    {
        var eventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
        var bookingRepo = new Mock<IBookingRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IBookingUnitOfWork>(MockBehavior.Strict);
        var validation = new Mock<IBookingValidationPipeline>(MockBehavior.Strict);

        var rejectingLock = new RejectingLock();

        var handler = new ReserveSeatCommandHandler(
            eventRepo.Object,
            bookingRepo.Object,
            unitOfWork.Object,
            validation.Object,
            rejectingLock,
            TimeProvider.System);

        var result = await handler.Handle(
            new ReserveSeatCommand(Guid.NewGuid(), Guid.NewGuid(), "A1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ReserveSeatErrors.SeatLockUnavailable);

        // No repository touched — strict mocks would have thrown otherwise.
        eventRepo.VerifyNoOtherCalls();
        bookingRepo.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
        validation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_Release_Lock_Even_When_Validation_Fails()
    {
        var lockProbe = new ProbingLock();

        var eventRepo = new Mock<IEventRepository>();
        eventRepo.Setup(repo => repo.GetByIdAsync(It.IsAny<EventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Booking.Service.Domain.Aggregates.Event?)null);

        var handler = new ReserveSeatCommandHandler(
            eventRepo.Object,
            Mock.Of<IBookingRepository>(),
            Mock.Of<IBookingUnitOfWork>(),
            Mock.Of<IBookingValidationPipeline>(),
            lockProbe,
            TimeProvider.System);

        var result = await handler.Handle(
            new ReserveSeatCommand(Guid.NewGuid(), Guid.NewGuid(), "A1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        lockProbe.AcquireCount.Should().Be(1);
        lockProbe.ReleaseCount.Should().Be(1);
    }

    private sealed class RejectingLock : ISeatReservationLock
    {
        public Task<ISeatReservationLockHandle?> TryAcquireAsync(
            EventId eventId,
            SeatNumber seatNumber,
            CancellationToken cancellationToken)
            => Task.FromResult<ISeatReservationLockHandle?>(null);
    }

    private sealed class ProbingLock : ISeatReservationLock
    {
        public int AcquireCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<ISeatReservationLockHandle?> TryAcquireAsync(
            EventId eventId,
            SeatNumber seatNumber,
            CancellationToken cancellationToken)
        {
            AcquireCount++;
            return Task.FromResult<ISeatReservationLockHandle?>(new ProbingHandle(this));
        }

        private sealed class ProbingHandle(ProbingLock owner) : ISeatReservationLockHandle
        {
            public ValueTask DisposeAsync()
            {
                owner.ReleaseCount++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
