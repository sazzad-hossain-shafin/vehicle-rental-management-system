using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Abstractions;

public interface IVehicleRepository
{
    /// <param name="registrationNumber">A registration number in normalized form; see <see cref="Vehicle.NormalizeRegistrationNumber"/>.</param>
    /// <returns>The vehicle, ready to be changed and saved, or null if there is none.</returns>
    Task<Vehicle?> GetByRegistrationNumberAsync(
        string registrationNumber,
        CancellationToken cancellationToken = default);

    /// <summary>All vehicles, ordered by registration number.</summary>
    Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds vehicles matching every filter that is set, ordered by registration number.
    /// Filtering is part of the repository so a database can do it in the query.
    /// </summary>
    Task<IReadOnlyList<Vehicle>> SearchAsync(
        VehicleSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a vehicle. It is stored when <see cref="IUnitOfWork.SaveChangesAsync"/> succeeds.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">
    /// The registration number is already used. Depending on the implementation this is
    /// raised here or when the changes are saved.
    /// </exception>
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
}
