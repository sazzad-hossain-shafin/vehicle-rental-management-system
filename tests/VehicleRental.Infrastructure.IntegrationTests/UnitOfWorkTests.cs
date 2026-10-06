using Microsoft.EntityFrameworkCore;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

public class UnitOfWorkTests : DatabaseTestBase
{
    public UnitOfWorkTests(PostgresFixture database) : base(database)
    {
    }

    [DatabaseFact]
    public async Task Changes_ArePersistedOnlyAfterSaveChanges()
    {
        await using var writer = Database.CreateSession();
        await writer.Vehicles.AddAsync(TestEntities.NewVehicle("ABC-123"));

        await using (var before = Database.CreateSession())
        {
            Assert.Null(await before.Vehicles.GetByRegistrationNumberAsync("ABC-123"));
        }

        await writer.UnitOfWork.SaveChangesAsync();

        await using var after = Database.CreateSession();
        Assert.NotNull(await after.Vehicles.GetByRegistrationNumberAsync("ABC-123"));
    }

    [DatabaseFact]
    public async Task UnsavedChanges_AreDiscardedWhenTheSessionIsDisposed()
    {
        await using (var session = Database.CreateSession())
        {
            await session.Vehicles.AddAsync(TestEntities.NewVehicle("ABC-123"));
            await session.Customers.AddAsync(TestEntities.NewCustomer());
        }

        await using var reader = Database.CreateSession();
        Assert.Empty(await reader.Vehicles.GetAllAsync());
        Assert.Null(await reader.Customers.GetByCustomerNumberAsync("C1"));
    }

    [DatabaseFact]
    public async Task OneSave_CommitsAVehicleACustomerAndARentalTogether()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123");
        var customer = TestEntities.NewCustomer();
        var rental = TestEntities.NewRental(vehicle, customer);

        await TestEntities.SaveAsync(Database, vehicle, customer, rental);

        await using var context = Database.CreateContext();
        Assert.Equal(1, await context.Vehicles.CountAsync());
        Assert.Equal(1, await context.Customers.CountAsync());
        Assert.Equal(1, await context.Rentals.CountAsync());
    }

    [DatabaseFact]
    public async Task AFailedSave_RollsBackEveryChangeInThatUnit()
    {
        await TestEntities.SaveAsync(Database, customer: TestEntities.NewCustomer("C1", "Alice"));

        await using (var session = Database.CreateSession())
        {
            // The vehicle would be valid on its own, but the customer number is a duplicate.
            await session.Vehicles.AddAsync(TestEntities.NewVehicle("ABC-123"));
            await session.Customers.AddAsync(TestEntities.NewCustomer("C1", "Impostor"));

            await Assert.ThrowsAsync<VehicleRental.Application.Exceptions.ConflictException>(
                () => session.UnitOfWork.SaveChangesAsync());
        }

        await using var reader = Database.CreateSession();
        Assert.Null(await reader.Vehicles.GetByRegistrationNumberAsync("ABC-123"));
        Assert.Equal("Alice", (await reader.Customers.GetByCustomerNumberAsync("C1"))!.Name);
    }

    [DatabaseFact]
    public async Task SaveChanges_WithACancelledToken_ThrowsAndSavesNothing()
    {
        using var cts = new CancellationTokenSource();
        await using var session = Database.CreateSession();
        await session.Vehicles.AddAsync(TestEntities.NewVehicle("ABC-123"));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.UnitOfWork.SaveChangesAsync(cts.Token));

        await using var reader = Database.CreateSession();
        Assert.Empty(await reader.Vehicles.GetAllAsync());
    }
}
