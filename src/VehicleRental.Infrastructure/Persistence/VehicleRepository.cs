using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence;

public sealed class VehicleRepository : IVehicleRepository
{
    private readonly VehicleRentalDbContext _db;

    public VehicleRepository(VehicleRentalDbContext db) => _db = db;

    // Tracked, because the caller may rent or return the vehicle and save.
    public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<Vehicle?> GetByRegistrationNumberAsync(
        string registrationNumber,
        CancellationToken cancellationToken = default) =>
        _db.Vehicles.FirstOrDefaultAsync(v => v.RegistrationNumber == registrationNumber, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Vehicles
            .AsNoTracking()
            .OrderBy(v => v.RegistrationNumber)
            .ToListAsync(cancellationToken);

    // Read-only, so untracked. Every filter becomes part of the SQL WHERE clause.
    public async Task<IReadOnlyList<Vehicle>> SearchAsync(
        VehicleSearchCriteria criteria,
        CancellationToken cancellationToken = default) =>
        await Filter(criteria)
            .OrderBy(v => v.RegistrationNumber)
            .ToListAsync(cancellationToken);

    // Two queries: the total count and one page. Both run in the database with the same filters.
    public async Task<PageResult<Vehicle>> SearchPageAsync(
        VehicleSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Vehicle> filtered = Filter(criteria);

        long total = await filtered.LongCountAsync(cancellationToken);

        List<Vehicle> items = await filtered
            .OrderBy(v => v.RegistrationNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new PageResult<Vehicle>(items, total);
    }

    // The INSERT happens in IUnitOfWork.SaveChangesAsync, where the unique index decides duplicates.
    public Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default)
    {
        _db.Vehicles.Add(vehicle);

        return Task.CompletedTask;
    }

    private IQueryable<Vehicle> Filter(VehicleSearchCriteria criteria)
    {
        IQueryable<Vehicle> query = _db.Vehicles.AsNoTracking();

        if (criteria.VehicleType is { } type)
        {
            query = query.Where(v => v.VehicleType == type);
        }

        if (criteria.MaximumDailyRate is { } maximumRate)
        {
            query = query.Where(v => v.DailyRate <= maximumRate);
        }

        if (criteria.Availability is { } availability)
        {
            query = query.Where(v => v.AvailabilityStatus == availability);
        }

        return query;
    }
}
