using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.InMemory;

/// <summary>
/// TEMPORARY in-memory storage, used until a database implementation exists. Rentals
/// are kept in the order they were added. Results are copies, so callers cannot change
/// the stored collection. It is not thread-safe.
/// </summary>
public sealed class InMemoryRentalRepository : IRentalRepository
{
    private readonly List<Rental> _rentals = new();

    /// <summary>The number of stored rentals.</summary>
    public int Count => _rentals.Count;

    public Task<Rental?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_rentals.FirstOrDefault(r => r.Id == id));
    }

    public Task<Rental?> GetActiveForVehicleAsync(
        string vehicleId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Rental? active = _rentals.FirstOrDefault(r =>
            r.Status == RentalStatus.Active &&
            string.Equals(r.Vehicle.Id, vehicleId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(active);
    }

    public Task<IReadOnlyList<Rental>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<Rental>>(_rentals.ToList());
    }

    public Task AddAsync(Rental rental, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _rentals.Add(rental);

        return Task.CompletedTask;
    }
}
