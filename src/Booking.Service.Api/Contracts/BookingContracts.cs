namespace Booking.Service.Api.Contracts;

public sealed record ReserveSeatRequest(Guid EventId, string SeatNumber);

public sealed record ConfirmBookingRequest(decimal Amount, string Currency);

public sealed record CancelBookingRequest(Guid? BookingId = null);
