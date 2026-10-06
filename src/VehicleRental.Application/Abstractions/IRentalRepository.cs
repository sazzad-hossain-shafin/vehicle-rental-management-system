using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Abstractions;

public interface IRentalRepository
{
    Task<Rental?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The vehicle's rental that has not been completed, or null if there is none.</summary>
    Task<Rental?> GetActiveForVehicleAsync(string vehicleId, CancellationToken cancellationToken = default);

    /// <summary>All rentals, oldest first.</summary>
    Task<IReadOnlyList<Rental>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Rental rental, CancellationToken cancellationToken = default);
}
