using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Abstractions;

public interface IRentalRepository
{
    /// <returns>The rental with its customer and vehicle loaded, or null if there is none.</returns>
    Task<Rental?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The vehicle's rental that has not been completed, with its customer and vehicle loaded,
    /// or null if there is none.
    /// </summary>
    Task<Rental?> GetActiveForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);

    /// <summary>All rentals with their customers and vehicles, oldest start date first.</summary>
    Task<IReadOnlyList<Rental>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a rental. It is stored when <see cref="IUnitOfWork.SaveChangesAsync"/> succeeds.
    /// </summary>
    Task AddAsync(Rental rental, CancellationToken cancellationToken = default);
}
