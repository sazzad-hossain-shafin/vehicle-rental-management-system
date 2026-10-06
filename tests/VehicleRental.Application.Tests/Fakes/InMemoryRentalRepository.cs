using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IRentalRepository"/>. Rentals are returned oldest start date
/// first, then in the order they were added. Results are copies.
/// </summary>
internal sealed class InMemoryRentalRepository : IRentalRepository
{
    private readonly List<Rental> _rentals = new();

    public int Count => _rentals.Count;

    public Task<Rental?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_rentals.FirstOrDefault(r => r.Id == id));
    }

    public Task<Rental?> GetActiveForVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Rental? active = _rentals.FirstOrDefault(r =>
            r.Status == RentalStatus.Active && r.Vehicle.Id == vehicleId);

        return Task.FromResult(active);
    }

    public Task<IReadOnlyList<Rental>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // OrderBy is stable, so rentals with the same start date stay in the order they were added.
        return Task.FromResult<IReadOnlyList<Rental>>(_rentals.OrderBy(r => r.StartDate).ToList());
    }

    public Task<PageResult<Rental>> GetPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ordered = _rentals.OrderBy(r => r.StartDate).ToList();

        return Task.FromResult(new PageResult<Rental>(ordered.Skip(skip).Take(take).ToList(), ordered.Count));
    }

    public Task AddAsync(Rental rental, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _rentals.Add(rental);

        return Task.CompletedTask;
    }
}
