using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.InMemory;

/// <summary>
/// TEMPORARY in-memory storage, used until a database implementation exists. Vehicle
/// IDs are unique, ignoring case. Results are copies, so callers cannot change the
/// stored collection. It is not thread-safe.
/// </summary>
public sealed class InMemoryVehicleRepository : IVehicleRepository
{
    private readonly List<Vehicle> _vehicles = new();

    /// <summary>The number of stored vehicles.</summary>
    public int Count => _vehicles.Count;

    public Task<Vehicle?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Find(id));
    }

    public Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<Vehicle>>(_vehicles.ToList());
    }

    public Task<IReadOnlyList<Vehicle>> SearchAsync(
        VehicleSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<Vehicle> query = _vehicles;

        if (criteria.VehicleType is not null)
        {
            query = query.Where(v => v.VehicleType == criteria.VehicleType);
        }

        if (criteria.MaximumDailyRate is not null)
        {
            query = query.Where(v => v.DailyRate <= criteria.MaximumDailyRate);
        }

        if (criteria.Availability is not null)
        {
            query = query.Where(v => v.AvailabilityStatus == criteria.Availability);
        }

        return Task.FromResult<IReadOnlyList<Vehicle>>(query.ToList());
    }

    public Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Find(vehicle.Id) is not null)
        {
            throw new ConflictException($"A vehicle with ID '{vehicle.Id}' already exists.");
        }

        _vehicles.Add(vehicle);

        return Task.CompletedTask;
    }

    private Vehicle? Find(string id) =>
        _vehicles.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
}
