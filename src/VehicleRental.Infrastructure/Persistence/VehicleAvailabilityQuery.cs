using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// "Which vehicles are free for these dates?", answered entirely in SQL with two NOT EXISTS conditions, so
/// there is no query per vehicle and paging happens in the database.
/// </summary>
public sealed class VehicleAvailabilityQuery : IVehicleAvailabilityQuery
{
    private readonly VehicleRentalDbContext _db;

    public VehicleAvailabilityQuery(VehicleRentalDbContext db) => _db = db;

    public async Task<bool> IsAvailableAsync(
        Guid vehicleId,
        DateOnly start,
        DateOnly end,
        DateOnly today,
        CancellationToken cancellationToken = default) =>
        await Free(_db.Vehicles.AsNoTracking().Where(v => v.Id == vehicleId), start, end, today)
            .AnyAsync(cancellationToken);

    public async Task<PageResult<Vehicle>> SearchAvailableAsync(
        VehicleType? vehicleType,
        decimal? maximumDailyRate,
        DateOnly start,
        DateOnly end,
        DateOnly today,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Vehicle> vehicles = _db.Vehicles.AsNoTracking();

        if (vehicleType is { } type)
        {
            vehicles = vehicles.Where(v => v.VehicleType == type);
        }

        if (maximumDailyRate is { } maximumRate)
        {
            vehicles = vehicles.Where(v => v.DailyRate <= maximumRate);
        }

        IQueryable<Vehicle> free = Free(vehicles, start, end, today);

        long total = await free.LongCountAsync(cancellationToken);

        List<Vehicle> items = await free
            .OrderBy(v => v.RegistrationNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new PageResult<Vehicle>(items, total);
    }

    /// <summary>
    /// Keeps the vehicles with no active reservation and no active rental overlapping <c>[start, end)</c>.
    /// An active rental occupies <c>[StartDate, ExpectedReturnDate)</c>, and an overdue one still has the vehicle
    /// out today, so it occupies at least until tomorrow.
    /// </summary>
    private IQueryable<Vehicle> Free(IQueryable<Vehicle> vehicles, DateOnly start, DateOnly end, DateOnly today)
    {
        DateOnly tomorrow = today.AddDays(1);

        return vehicles.Where(v =>
            !_db.Reservations.Any(r =>
                r.Vehicle.Id == v.Id
                && r.Status == ReservationStatus.Active
                && r.StartDate < end
                && start < r.EndDate)
            && !_db.Rentals.Any(r =>
                r.Vehicle.Id == v.Id
                && r.Status == RentalStatus.Active
                && r.StartDate < end
                && (r.ExpectedReturnDate > start || tomorrow > start)));
    }
}
