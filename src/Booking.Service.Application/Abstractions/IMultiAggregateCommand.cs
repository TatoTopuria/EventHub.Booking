namespace Booking.Service.Application.Abstractions;

/// <summary>
/// Marks commands that modify multiple aggregates and must execute in an explicit transaction.
/// </summary>
public interface IMultiAggregateCommand
{
}
