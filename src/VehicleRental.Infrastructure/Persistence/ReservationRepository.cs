using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Infrastructure.Persistence;

public sealed class ReservationRepository : IReservationRepository
{
    private readonly VehicleRentalDbContext _db;

    public ReservationRepository(VehicleRentalDbContext db) => _db = db;

    // Customer and vehicle come in the same query, so reading reservations never costs a query per row.
    // Tracked, because the reservation may be cancelled or picked up and saved.
    public Task<Reservation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Reservations
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<PageResult<Reservation>> GetPageAsync(
        ReservationStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Reservation> query = _db.Reservations;

        if (status is { } wanted)
        {
            query = query.Where(r => r.Status == wanted);
        }

        return await PageAsync(query, skip, take, cancellationToken);
    }

    // The customer filter is part of the SQL WHERE clause, so a customer's page never contains other
    // customers' rows and the count only counts theirs.
    public Task<PageResult<Reservation>> GetPageForCustomerAsync(
        Guid customerId,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        PageAsync(_db.Reservations.Where(r => r.Customer.Id == customerId), skip, take, cancellationToken);

    // Half-open overlap: [r.Start, r.End) and [start, end) share a day when each starts before the other ends.
    public Task<bool> HasActiveOverlapAsync(
        Guid vehicleId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default) =>
        _db.Reservations.AnyAsync(
            r => r.Vehicle.Id == vehicleId
                 && r.Status == ReservationStatus.Active
                 && r.StartDate < end
                 && start < r.EndDate,
            cancellationToken);

    // The INSERT happens in IUnitOfWork.SaveChangesAsync, where the database's exclusion constraint decides overlaps.
    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        _db.Reservations.Add(reservation);

        return Task.CompletedTask;
    }

    // Two queries: the count, and one page (with customers and vehicles) in a stable order.
    private static async Task<PageResult<Reservation>> PageAsync(
        IQueryable<Reservation> query,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        long total = await query.LongCountAsync(cancellationToken);

        List<Reservation> items = await query
            .AsNoTrackingWithIdentityResolution()
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.Id) // time-ordered UUIDs, so reservations that start the same day keep creation order
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new PageResult<Reservation>(items, total);
    }
}
