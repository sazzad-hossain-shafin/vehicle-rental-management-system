using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

public class RentalPersistenceTests : DatabaseTestBase
{
    public RentalPersistenceTests(PostgresFixture database) : base(database)
    {
    }

    [DatabaseFact]
    public async Task SavedRental_ReloadsWithItsCustomerAndVehicle()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123");
        var customer = TestEntities.NewCustomer("C1", "Alice");
        var rental = TestEntities.NewRental(vehicle, customer);
        await TestEntities.SaveAsync(Database, vehicle, customer, rental);

        await using var session = Database.CreateSession();
        var loaded = await session.Rentals.GetByIdAsync(rental.Id);

        Assert.NotNull(loaded);
        Assert.Equal(customer.Id, loaded.Customer.Id);
        Assert.Equal("Alice", loaded.Customer.Name);
        Assert.Equal(vehicle.Id, loaded.Vehicle.Id);
        Assert.Equal("ABC-123", loaded.Vehicle.RegistrationNumber);
        Assert.Equal(VehicleAvailabilityStatus.Rented, loaded.Vehicle.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task SavedRental_KeepsItsPriceSnapshotAndDatesAfterReload()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123", dailyRate: 59.99m);
        var customer = TestEntities.NewCustomer();
        var rental = TestEntities.NewRental(vehicle, customer, days: 3, strategy: new DiscountPricingStrategy());
        await TestEntities.SaveAsync(Database, vehicle, customer, rental);

        await using var session = Database.CreateSession();
        var loaded = await session.Rentals.GetByIdAsync(rental.Id);

        Assert.Equal(TestEntities.Start, loaded!.StartDate);
        Assert.Equal(TestEntities.Start.AddDays(3), loaded.ExpectedReturnDate);
        Assert.Null(loaded.ActualReturnDate);
        Assert.Equal(RentalStatus.Active, loaded.Status);
        Assert.Equal(59.99m, loaded.DailyRateAtRental);
        Assert.Equal(3, loaded.BillableDays);
        Assert.Equal(new DiscountPricingStrategy().Name, loaded.PricingDescription);
        Assert.Equal(161.97m, loaded.TotalCost); // 59.99 x 3 x 0.90, rounded to cents
    }

    [DatabaseFact]
    public async Task CompletedRental_SurvivesReload_AndTheVehicleIsAvailableAgain()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123");
        var customer = TestEntities.NewCustomer();
        var rental = TestEntities.NewRental(vehicle, customer, days: 3);
        await TestEntities.SaveAsync(Database, vehicle, customer, rental);

        await using (var session = Database.CreateSession())
        {
            var active = await session.Rentals.GetActiveForVehicleAsync(vehicle.Id);
            active!.Complete(TestEntities.Start.AddDays(2));
            await session.UnitOfWork.SaveChangesAsync();
        }

        await using var reader = Database.CreateSession();
        var loaded = await reader.Rentals.GetByIdAsync(rental.Id);

        Assert.Equal(RentalStatus.Completed, loaded!.Status);
        Assert.Equal(TestEntities.Start.AddDays(2), loaded.ActualReturnDate);
        Assert.Equal(VehicleAvailabilityStatus.Available, loaded.Vehicle.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task HistoricalTotal_StaysTheSame_WhenTheVehicleIsRentedAgainUnderDifferentPricing()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123", dailyRate: 100m);
        var alice = TestEntities.NewCustomer("C1", "Alice");
        var first = TestEntities.NewRental(vehicle, alice, days: 5);
        await TestEntities.SaveAsync(Database, vehicle, alice, first);

        await using (var session = Database.CreateSession())
        {
            var active = await session.Rentals.GetActiveForVehicleAsync(vehicle.Id);
            active!.Complete(TestEntities.Start.AddDays(5));
            await session.UnitOfWork.SaveChangesAsync();
        }

        await using (var session = Database.CreateSession())
        {
            var reloadedVehicle = await session.Vehicles.GetByRegistrationNumberAsync("ABC-123");
            var bob = TestEntities.NewCustomer("C2", "Bob");
            var second = TestEntities.NewRental(
                reloadedVehicle!, bob, days: 10, start: TestEntities.Start.AddDays(5), strategy: new LongTermPricingStrategy());
            await session.Customers.AddAsync(bob);
            await session.Rentals.AddAsync(second);
            await session.UnitOfWork.SaveChangesAsync();
        }

        await using var reader = Database.CreateSession();
        var history = await reader.Rentals.GetAllAsync();

        Assert.Equal(new[] { 500m, 800m }, history.Select(r => r.TotalCost));
        Assert.Equal(new[] { 5, 10 }, history.Select(r => r.BillableDays));
    }

    [DatabaseFact]
    public async Task GetActiveForVehicle_FindsTheActiveRental_AndIsNullAfterCompletion()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123");
        var customer = TestEntities.NewCustomer();
        var rental = TestEntities.NewRental(vehicle, customer);
        await TestEntities.SaveAsync(Database, vehicle, customer, rental);

        await using (var session = Database.CreateSession())
        {
            var active = await session.Rentals.GetActiveForVehicleAsync(vehicle.Id);

            Assert.Equal(rental.Id, active!.Id);
            Assert.Equal("Alice", active.Customer.Name);

            active.Complete(TestEntities.Start.AddDays(3));
            await session.UnitOfWork.SaveChangesAsync();
        }

        await using var reader = Database.CreateSession();
        Assert.Null(await reader.Rentals.GetActiveForVehicleAsync(vehicle.Id));
    }

    [DatabaseFact]
    public async Task GetActiveForVehicle_ForAnotherVehicle_ReturnsNull()
    {
        var rented = TestEntities.NewVehicle("ABC-123");
        var other = TestEntities.NewVehicle("DEF-456");
        var customer = TestEntities.NewCustomer();
        await TestEntities.SaveAsync(Database, other);
        await TestEntities.SaveAsync(Database, rented, customer, TestEntities.NewRental(rented, customer));

        await using var session = Database.CreateSession();

        Assert.Null(await session.Rentals.GetActiveForVehicleAsync(other.Id));
    }

    [DatabaseFact]
    public async Task GetAll_ReturnsRentalsOldestStartDateFirst_WithTheirCustomersAndVehicles()
    {
        var v1 = TestEntities.NewVehicle("ABC-123");
        var v2 = TestEntities.NewVehicle("DEF-456");
        var alice = TestEntities.NewCustomer("C1", "Alice");
        var bob = TestEntities.NewCustomer("C2", "Bob");
        var later = TestEntities.NewRental(v1, alice, start: TestEntities.Start.AddDays(10));
        var earlier = TestEntities.NewRental(v2, bob, start: TestEntities.Start);
        await TestEntities.SaveAsync(Database, v1, alice, later);
        await TestEntities.SaveAsync(Database, v2, bob, earlier);

        await using var session = Database.CreateSession();
        var history = await session.Rentals.GetAllAsync();

        Assert.Equal(new[] { earlier.Id, later.Id }, history.Select(r => r.Id));
        Assert.Equal(new[] { "Bob", "Alice" }, history.Select(r => r.Customer.Name));
        Assert.Equal(new[] { "DEF-456", "ABC-123" }, history.Select(r => r.Vehicle.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task GetAll_WithNoRentals_ReturnsEmptyList()
    {
        await using var session = Database.CreateSession();

        Assert.Empty(await session.Rentals.GetAllAsync());
    }

    [DatabaseFact]
    public async Task GetById_WithUnknownId_ReturnsNull()
    {
        await using var session = Database.CreateSession();

        Assert.Null(await session.Rentals.GetByIdAsync(Guid.NewGuid()));
    }
}
