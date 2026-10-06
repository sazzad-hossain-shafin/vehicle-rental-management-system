using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;
using VehicleRental.Infrastructure;

namespace VehicleRental.Infrastructure.IntegrationTests.Support;

internal static class TestEntities
{
    public static readonly DateOnly Start = new(2026, 10, 1);

    public static Vehicle NewVehicle(
        string registration = "ABC-123",
        decimal dailyRate = 100m,
        VehicleType type = VehicleType.Car) =>
        new(registration, "Toyota", "Corolla", 2022, type, dailyRate);

    public static Customer NewCustomer(string number = "C1", string name = "Alice") =>
        new(number, name);

    public static Rental NewRental(
        Vehicle vehicle,
        Customer customer,
        int days = 3,
        DateOnly? start = null,
        IVehiclePricingStrategy? strategy = null)
    {
        DateOnly startDate = start ?? Start;

        return Rental.Start(
            customer,
            vehicle,
            startDate,
            startDate.AddDays(days),
            strategy ?? new NormalPricingStrategy());
    }

    /// <summary>Saves entities in their own session, as an earlier run of the application would have.</summary>
    public static async Task SaveAsync(PostgresFixture database, Vehicle? vehicle = null, Customer? customer = null, Rental? rental = null)
    {
        await using PersistenceSession session = database.CreateSession();

        if (vehicle is not null)
        {
            await session.Vehicles.AddAsync(vehicle);
        }

        if (customer is not null)
        {
            await session.Customers.AddAsync(customer);
        }

        if (rental is not null)
        {
            await session.Rentals.AddAsync(rental);
        }

        await session.UnitOfWork.SaveChangesAsync();
    }
}
