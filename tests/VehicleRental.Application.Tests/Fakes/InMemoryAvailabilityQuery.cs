using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IVehicleAvailabilityQuery"/>, built on the other in-memory repositories. It applies
/// the same rule as the database query: a vehicle is free when no active reservation overlaps the period and no
/// active rental occupies it (an overdue rental still occupies today).
/// </summary>
internal sealed class InMemoryAvailabilityQuery : IVehicleAvailabilityQuery
{
    private readonly InMemoryVehicleRepository _vehicles;
    private readonly InMemoryReservationRepository _reservations;
    private readonly InMemoryRentalRepository _rentals;

    public InMemoryAvailabilityQuery(
        InMemoryVehicleRepository vehicles,
        InMemoryReservationRepository reservations,
        InMemoryRentalRepository rentals)
    {
        _vehicles = vehicles;
        _reservations = reservations;
        _rentals = rentals;
    }

    public async Task<bool> IsAvailableAsync(
        Guid vehicleId,
        DateOnly start,
        DateOnly end,
        DateOnly today,
        CancellationToken cancellationToken = default) =>
        !HasReservation(vehicleId, start, end) && !await HasRentalAsync(vehicleId, start, end, today);

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
        var free = new List<Vehicle>();

        foreach (Vehicle vehicle in (await _vehicles.GetAllAsync(cancellationToken)).OrderBy(v => v.RegistrationNumber, StringComparer.Ordinal))
        {
            if ((vehicleType is null || vehicle.VehicleType == vehicleType)
                && (maximumDailyRate is null || vehicle.DailyRate <= maximumDailyRate)
                && await IsAvailableAsync(vehicle.Id, start, end, today, cancellationToken))
            {
                free.Add(vehicle);
            }
        }

        return new PageResult<Vehicle>(free.Skip(skip).Take(take).ToList(), free.Count);
    }

    private bool HasReservation(Guid vehicleId, DateOnly start, DateOnly end) =>
        _reservations.All.Any(r =>
            r.Vehicle.Id == vehicleId && r.Status == ReservationStatus.Active && r.OverlapsPeriod(start, end));

    private async Task<bool> HasRentalAsync(Guid vehicleId, DateOnly start, DateOnly end, DateOnly today)
    {
        Rental? active = await _rentals.GetActiveForVehicleAsync(vehicleId);

        return active is not null
               && active.StartDate < end
               && (active.ExpectedReturnDate > start || today.AddDays(1) > start);
    }
}
