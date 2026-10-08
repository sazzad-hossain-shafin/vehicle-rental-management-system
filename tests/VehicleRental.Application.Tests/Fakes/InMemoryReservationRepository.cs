using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IReservationRepository"/>. Reservations are returned earliest start date first, then in
/// the order they were added. Unlike the real database it has no exclusion constraint, so a test that wants to see
/// the database backstop uses the PostgreSQL integration tests instead.
/// </summary>
internal sealed class InMemoryReservationRepository : IReservationRepository
{
    private readonly List<Reservation> _reservations = new();

    public IReadOnlyList<Reservation> All => _reservations;

    public Task<Reservation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_reservations.FirstOrDefault(r => r.Id == id));
    }

    public Task<PageResult<Reservation>> GetPageAsync(
        ReservationStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var matching = _reservations
            .Where(r => status is null || r.Status == status)
            .OrderBy(r => r.StartDate)
            .ToList();

        return Task.FromResult(new PageResult<Reservation>(matching.Skip(skip).Take(take).ToList(), matching.Count));
    }

    public Task<PageResult<Reservation>> GetPageForCustomerAsync(
        Guid customerId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var own = _reservations.Where(r => r.Customer.Id == customerId).OrderBy(r => r.StartDate).ToList();

        return Task.FromResult(new PageResult<Reservation>(own.Skip(skip).Take(take).ToList(), own.Count));
    }

    public Task<bool> HasActiveOverlapAsync(
        Guid vehicleId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_reservations.Any(r =>
            r.Vehicle.Id == vehicleId && r.Status == ReservationStatus.Active && r.OverlapsPeriod(start, end)));
    }

    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _reservations.Add(reservation);

        return Task.CompletedTask;
    }
}
