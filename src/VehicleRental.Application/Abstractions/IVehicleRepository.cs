using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Abstractions;

public interface IVehicleRepository
{
    Task<Vehicle?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds vehicles matching every filter that is set. Filtering is part of the
    /// repository so a database can do it in the query instead of in memory.
    /// </summary>
    Task<IReadOnlyList<Vehicle>> SearchAsync(
        VehicleSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <exception cref="Exceptions.ConflictException">A vehicle with the same ID already exists.</exception>
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
}
