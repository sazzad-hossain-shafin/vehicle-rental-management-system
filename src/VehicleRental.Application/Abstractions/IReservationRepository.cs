using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Abstractions;

public interface IReservationRepository
{
    /// <returns>The reservation with its customer and vehicle loaded, ready to be changed and saved, or null.</returns>
    Task<Reservation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of reservations (optionally only those with a given status), earliest start date first, with
    /// customers and vehicles and the total number that match. Filtering and paging are done by the database.
    /// </summary>
    Task<PageResult<Reservation>> GetPageAsync(
        ReservationStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a single customer's reservations, earliest start date first, with the total number of that
    /// customer's reservations. The customer filter is part of the database query.
    /// </summary>
    Task<PageResult<Reservation>> GetPageForCustomerAsync(
        Guid customerId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the vehicle has an active reservation overlapping <c>[start, end)</c> (half-open: a reservation
    /// ending on <paramref name="start"/> does not overlap).
    /// </summary>
    Task<bool> HasActiveOverlapAsync(
        Guid vehicleId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a reservation. It is stored when <see cref="IUnitOfWork.SaveChangesAsync"/> succeeds.
    /// </summary>
    Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default);
}
