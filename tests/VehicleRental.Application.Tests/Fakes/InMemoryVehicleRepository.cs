using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IVehicleRepository"/>. It mirrors the contract the real EF Core
/// repository is tested against: unique registration numbers and results ordered by
/// registration number. Results are copies, so callers cannot change the stored collection.
/// </summary>
internal sealed class InMemoryVehicleRepository : IVehicleRepository
{
    private readonly List<Vehicle> _vehicles = new();

    public int Count => _vehicles.Count;

    public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_vehicles.FirstOrDefault(v => v.Id == id));
    }

    public Task<PageResult<Vehicle>> SearchPageAsync(
        VehicleSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var all = SearchAsync(criteria, cancellationToken).Result;

        return Task.FromResult(new PageResult<Vehicle>(all.Skip(skip).Take(take).ToList(), all.Count));
    }

    public Task<Vehicle?> GetByRegistrationNumberAsync(
        string registrationNumber,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Find(registrationNumber));
    }

    public Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<Vehicle>>(Ordered(_vehicles));
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

        return Task.FromResult<IReadOnlyList<Vehicle>>(Ordered(query));
    }

    public Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Find(vehicle.RegistrationNumber) is not null)
        {
            throw new ConflictException(
                $"A vehicle with registration number '{vehicle.RegistrationNumber}' already exists.");
        }

        _vehicles.Add(vehicle);

        return Task.CompletedTask;
    }

    private Vehicle? Find(string registrationNumber) =>
        _vehicles.FirstOrDefault(v => v.RegistrationNumber == registrationNumber);

    private static List<Vehicle> Ordered(IEnumerable<Vehicle> vehicles) =>
        vehicles.OrderBy(v => v.RegistrationNumber, StringComparer.Ordinal).ToList();
}
