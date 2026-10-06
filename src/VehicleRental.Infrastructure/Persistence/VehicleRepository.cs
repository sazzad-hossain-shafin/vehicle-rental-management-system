using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence;

public sealed class VehicleRepository : IVehicleRepository
{
    private readonly VehicleRentalDbContext _db;

    public VehicleRepository(VehicleRentalDbContext db) => _db = db;

    // Tracked, because the caller may rent or return the vehicle and save.
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
        CancellationToken cancellationToken = default)
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

        return await query.OrderBy(v => v.RegistrationNumber).ToListAsync(cancellationToken);
    }

    // The INSERT happens in IUnitOfWork.SaveChangesAsync, where the unique index decides duplicates.
    public Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default)
    {
        _db.Vehicles.Add(vehicle);

        return Task.CompletedTask;
    }
}
