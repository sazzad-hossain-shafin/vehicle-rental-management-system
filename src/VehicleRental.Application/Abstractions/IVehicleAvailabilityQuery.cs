using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Abstractions;

/// <summary>
/// Answers "which vehicles are free for these dates?". A vehicle is free for <c>[start, end)</c> when it has no
/// active reservation overlapping the period and no active rental occupying it.
/// </summary>
/// <remarks>
/// An active rental occupies <c>[StartDate, ExpectedReturnDate)</c>. A rental that is overdue (not returned by
/// its expected return date) still has the vehicle out today, so it occupies at least today. This is separate
/// from the vehicle's <c>Available</c>/<c>Rented</c> status, which says only whether the vehicle is out right now.
/// </remarks>
public interface IVehicleAvailabilityQuery
{
    /// <param name="today">The current date, used for overdue rentals.</param>
    Task<bool> IsAvailableAsync(
        Guid vehicleId,
        DateOnly start,
        DateOnly end,
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the vehicles that are free for the whole period, optionally filtered by type and maximum
    /// daily rate, ordered by registration number, with the total number that match. Done in the database.
    /// </summary>
    Task<PageResult<Vehicle>> SearchAvailableAsync(
        VehicleType? vehicleType,
        decimal? maximumDailyRate,
        DateOnly start,
        DateOnly end,
        DateOnly today,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
