using VehicleRental.Application.Customers;
using VehicleRental.Application.Tests.Fakes;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests;

/// <summary>A clock that only moves when a test moves it.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void AdvanceDays(int days) => _now = _now.AddDays(days);

    public void Advance(TimeSpan time) => _now = _now.Add(time);
}

/// <summary>
/// The services wired to fresh in-memory repositories and a fixed clock (1 October 2026).
/// </summary>
internal sealed class TestApp
{
    public static readonly DateOnly Today = new(2026, 10, 1);

    public FixedTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public InMemoryVehicleRepository VehicleRepository { get; } = new();
    public InMemoryCustomerRepository CustomerRepository { get; } = new();
    public InMemoryRentalRepository RentalRepository { get; } = new();
    public InMemoryReservationRepository ReservationRepository { get; } = new();

    public VehicleService Vehicles { get; }
    public CustomerService Customers { get; }
    public RentalService Rentals { get; }
    public ReservationService Reservations { get; }

    public TestApp()
    {
        var unitOfWork = new InMemoryUnitOfWork();

        Vehicles = new VehicleService(VehicleRepository, unitOfWork);
        Customers = new CustomerService(CustomerRepository, unitOfWork);
        Rentals = new RentalService(
            VehicleRepository, CustomerRepository, RentalRepository, ReservationRepository, unitOfWork, Clock);
        Reservations = new ReservationService(
            VehicleRepository,
            CustomerRepository,
            ReservationRepository,
            RentalRepository,
            new InMemoryAvailabilityQuery(VehicleRepository, ReservationRepository, RentalRepository),
            unitOfWork,
            Clock);
    }

    public Task<VehicleDto> AddVehicleAsync(
        string id = "V1",
        decimal dailyRate = 100m,
        VehicleType type = VehicleType.Car) =>
        Vehicles.AddVehicleAsync(new AddVehicleRequest(id, "Toyota", "Corolla", 2022, type, dailyRate));

    public Task<RentalDto> StartRentalAsync(
        string vehicleId = "V1",
        string customerId = "C1",
        string customerName = "Alice",
        int days = 3,
        bool promotion = false) =>
        Rentals.StartRentalAsync(new StartRentalRequest(vehicleId, customerId, customerName, days, promotion));
}
