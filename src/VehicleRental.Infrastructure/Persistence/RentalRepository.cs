using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Infrastructure.Persistence;

public sealed class RentalRepository : IRentalRepository
{
    private readonly VehicleRentalDbContext _db;

    public RentalRepository(VehicleRentalDbContext db) => _db = db;

    // Customer and vehicle are loaded in the same query (one JOIN each), so reading rentals
    // never triggers a query per row. Tracked, because the rental may be completed and saved.
    public Task<Rental?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Rentals
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Rental?> GetActiveForVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default) =>
        _db.Rentals
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .FirstOrDefaultAsync(
                r => r.Vehicle.Id == vehicleId && r.Status == RentalStatus.Active,
                cancellationToken);

    // Read-only, so untracked. Identity resolution keeps one Vehicle/Customer object per row
    // instead of a copy for every rental that references it.
    public async Task<IReadOnlyList<Rental>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Rentals
            .AsNoTrackingWithIdentityResolution()
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.Id) // time-ordered UUIDs, so rentals on the same day stay in creation order
            .ToListAsync(cancellationToken);

    // Two queries: the total count, and one page (with its customers and vehicles) in a stable order.
    public async Task<PageResult<Rental>> GetPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        long total = await _db.Rentals.LongCountAsync(cancellationToken);

        List<Rental> items = await _db.Rentals
            .AsNoTrackingWithIdentityResolution()
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new PageResult<Rental>(items, total);
    }

    // The INSERT happens in IUnitOfWork.SaveChangesAsync.
    public Task AddAsync(Rental rental, CancellationToken cancellationToken = default)
    {
        _db.Rentals.Add(rental);

        return Task.CompletedTask;
    }
}
