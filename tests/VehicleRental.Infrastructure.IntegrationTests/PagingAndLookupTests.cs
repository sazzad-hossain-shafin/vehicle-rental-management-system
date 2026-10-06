using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// Lookups by permanent ID and database-side paging, which the web API relies on.
/// </summary>
public class PagingAndLookupTests : DatabaseTestBase
{
    public PagingAndLookupTests(PostgresFixture database) : base(database)
    {
    }

    [DatabaseFact]
    public async Task Vehicle_CanBeLoadedByItsId()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123");
        await TestEntities.SaveAsync(Database, vehicle: vehicle);

        await using var session = Database.CreateSession();

        Assert.Equal("ABC-123", (await session.Vehicles.GetByIdAsync(vehicle.Id))!.RegistrationNumber);
        Assert.Null(await session.Vehicles.GetByIdAsync(Guid.NewGuid()));
    }

    [DatabaseFact]
    public async Task Customer_CanBeLoadedByItsId()
    {
        var customer = TestEntities.NewCustomer("C1", "Alice");
        await TestEntities.SaveAsync(Database, customer: customer);

        await using var session = Database.CreateSession();

        Assert.Equal("Alice", (await session.Customers.GetByIdAsync(customer.Id))!.Name);
        Assert.Null(await session.Customers.GetByIdAsync(Guid.NewGuid()));
    }

    private async Task SaveVehiclesAsync(int count)
    {
        await using var session = Database.CreateSession();

        for (int i = 1; i <= count; i++)
        {
            var type = i % 2 == 0 ? VehicleType.Car : VehicleType.Van;
            await session.Vehicles.AddAsync(TestEntities.NewVehicle($"VEH-{i:000}", 50m + i, type));
        }

        await session.UnitOfWork.SaveChangesAsync();
    }

    [DatabaseFact]
    public async Task VehiclePage_ReturnsTheSliceAndTheTotal_InRegistrationOrder()
    {
        await SaveVehiclesAsync(12);

        await using var session = Database.CreateSession();
        var page = await session.Vehicles.SearchPageAsync(new VehicleSearchCriteria(), skip: 5, take: 4);

        Assert.Equal(12, page.TotalCount);
        Assert.Equal(new[] { "VEH-006", "VEH-007", "VEH-008", "VEH-009" }, page.Items.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task VehiclePage_AppliesTheFiltersBeforeCountingAndPaging()
    {
        await SaveVehiclesAsync(12); // even numbers are cars: 002, 004, ... 012

        await using var session = Database.CreateSession();
        var page = await session.Vehicles.SearchPageAsync(
            new VehicleSearchCriteria(VehicleType: VehicleType.Car, MaximumDailyRate: 60m), skip: 1, take: 2);

        // Cars up to $60: 002, 004, 006, 008, 010 (rates 52, 54, 56, 58, 60).
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(new[] { "VEH-004", "VEH-006" }, page.Items.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task VehiclePage_BeyondTheEnd_IsEmptyWithTheTotal()
    {
        await SaveVehiclesAsync(3);

        await using var session = Database.CreateSession();
        var page = await session.Vehicles.SearchPageAsync(new VehicleSearchCriteria(), skip: 50, take: 10);

        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalCount);
    }

    [DatabaseFact]
    public async Task RentalPage_ReturnsTheSliceOldestFirst_WithCustomersAndVehicles()
    {
        for (int i = 1; i <= 5; i++)
        {
            var vehicle = TestEntities.NewVehicle($"VEH-{i:000}");
            var customer = TestEntities.NewCustomer($"C{i}", $"Person {i}");
            var rental = TestEntities.NewRental(vehicle, customer, start: TestEntities.Start.AddDays(i));
            await TestEntities.SaveAsync(Database, vehicle, customer, rental);
        }

        await using var session = Database.CreateSession();
        var page = await session.Rentals.GetPageAsync(skip: 1, take: 2);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(new[] { "VEH-002", "VEH-003" }, page.Items.Select(r => r.Vehicle.RegistrationNumber));
        Assert.Equal(new[] { "Person 2", "Person 3" }, page.Items.Select(r => r.Customer.Name));
    }

    [DatabaseFact]
    public async Task RentalPage_WithNoRentals_IsEmpty()
    {
        await using var session = Database.CreateSession();
        var page = await session.Rentals.GetPageAsync(skip: 0, take: 20);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }
}
