using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Persistence.Models;

using BookingAggregate = Booking.Service.Domain.Aggregates.Booking;

namespace Booking.Service.Infrastructure.Persistence.Mappers;

internal static class BookingMapper
{
    public static BookingAggregate ToDomain(BookingEntity entity)
    {
        var booking = BookingAggregate.Create(
            entity.Id,
            new EventId(entity.EventId),
            new CustomerId(entity.CustomerId),
            entity.CreatedAtUtc).Value;

        foreach (var reservedSeat in entity.ReservedSeats)
        {
            booking.ReserveSeat(SeatNumber.Create(reservedSeat.SeatNumber).Value, entity.CreatedAtUtc);
        }

        if (entity.IsPaid)
        {
            booking.RegisterPayment(new Money(entity.PaymentAmount ?? 0, entity.PaymentCurrency ?? "USD"));
        }

        switch ((BookingStatus)entity.Status)
        {
            case BookingStatus.Confirmed:
                booking.Confirm(entity.ConfirmedAtUtc ?? entity.CreatedAtUtc);
                break;
            case BookingStatus.Expired:
                booking.ExpireIfUnpaid(entity.CreatedAtUtc.AddMinutes(11));
                break;
            case BookingStatus.Cancelled:
                booking.Cancel(entity.CreatedAtUtc.AddMinutes(1));
                break;
        }

        booking.ClearDomainEvents();
        return booking;
    }

    public static BookingEntity ToEntity(BookingAggregate booking)
    {
        return new BookingEntity
        {
            Id = booking.Id,
            EventId = booking.EventId.Value,
            CustomerId = booking.CustomerId.Value,
            Status = (int)booking.Status,
            CreatedAtUtc = booking.CreatedAtUtc,
            ConfirmedAtUtc = booking.ConfirmedAtUtc,
            IsPaid = booking.IsPaid,
            PaymentAmount = booking.PaymentAmount?.Amount,
            PaymentCurrency = booking.PaymentAmount?.Currency,
            ReservedSeats = booking.ReservedSeats
                .Select(seat => new BookingReservedSeatEntity { BookingId = booking.Id, SeatNumber = seat.Value })
                .ToList()
        };
    }

    public static void Apply(BookingAggregate booking, BookingEntity entity)
    {
        entity.EventId = booking.EventId.Value;
        entity.CustomerId = booking.CustomerId.Value;
        entity.Status = (int)booking.Status;
        entity.CreatedAtUtc = booking.CreatedAtUtc;
        entity.ConfirmedAtUtc = booking.ConfirmedAtUtc;
        entity.IsPaid = booking.IsPaid;
        entity.PaymentAmount = booking.PaymentAmount?.Amount;
        entity.PaymentCurrency = booking.PaymentAmount?.Currency;

        entity.ReservedSeats.Clear();
        foreach (var seat in booking.ReservedSeats)
        {
            entity.ReservedSeats.Add(new BookingReservedSeatEntity { BookingId = booking.Id, SeatNumber = seat.Value });
        }
    }
}
