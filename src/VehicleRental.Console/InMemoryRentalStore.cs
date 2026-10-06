using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.ConsoleApp;

/// <summary>
/// Temporary in-memory storage for the console client. It only holds and looks up
/// domain objects; all business rules live in the Domain project. It will be
/// replaced by persistence and application services in later phases.
/// </summary>
internal sealed class InMemoryRentalStore
{
    private readonly List<Vehicle> _vehicles = new();
    private readonly List<Rental> _rentals = new();

    public IReadOnlyList<Vehicle> Vehicles => _vehicles;

    public IReadOnlyList<Rental> Rentals => _rentals;

    /// <exception cref="InvalidOperationException">A vehicle with the same ID already exists.</exception>
    public void AddVehicle(Vehicle vehicle)
    {
        if (FindVehicle(vehicle.Id) is not null)
        {
            throw new InvalidOperationException($"A vehicle with ID '{vehicle.Id}' already exists.");
        }

        _vehicles.Add(vehicle);
    }

    public void AddRental(Rental rental) => _rentals.Add(rental);

    public Vehicle? FindVehicle(string id) =>
        _vehicles.FirstOrDefault(v => v.Id == id.Trim());

    public Rental? FindActiveRental(Vehicle vehicle) =>
        _rentals.FirstOrDefault(r => r.Vehicle == vehicle && r.Status == RentalStatus.Active);

    public IReadOnlyList<Vehicle> FindByType(VehicleType type) =>
        _vehicles.Where(v => v.VehicleType == type).ToList();

    public IReadOnlyList<Vehicle> FindByMaximumRate(decimal maxRate) =>
        _vehicles.Where(v => v.DailyRate <= maxRate).ToList();
}
