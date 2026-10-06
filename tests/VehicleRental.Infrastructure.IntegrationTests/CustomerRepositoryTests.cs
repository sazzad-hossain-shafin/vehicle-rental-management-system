using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

public class CustomerRepositoryTests : DatabaseTestBase
{
    public CustomerRepositoryTests(PostgresFixture database) : base(database)
    {
    }

    [DatabaseFact]
    public async Task AddedCustomer_CanBeReloadedByCustomerNumber()
    {
        var customer = TestEntities.NewCustomer("c-100", "Alice");
        await TestEntities.SaveAsync(Database, customer: customer);

        await using var session = Database.CreateSession();
        var loaded = await session.Customers.GetByCustomerNumberAsync(Customer.NormalizeCustomerNumber("c-100"));

        Assert.NotNull(loaded);
        Assert.Equal(customer.Id, loaded.Id);
        Assert.Equal("C-100", loaded.CustomerNumber);
        Assert.Equal("Alice", loaded.Name);
    }

    [DatabaseFact]
    public async Task GetByCustomerNumber_WithUnknownNumber_ReturnsNull()
    {
        await using var session = Database.CreateSession();

        Assert.Null(await session.Customers.GetByCustomerNumberAsync("NOPE"));
    }

    [DatabaseFact]
    public async Task AddingTheSameCustomerNumberTwice_IsRejectedAsAConflict()
    {
        await TestEntities.SaveAsync(Database, customer: TestEntities.NewCustomer("C1", "Alice"));

        await using var session = Database.CreateSession();
        await session.Customers.AddAsync(TestEntities.NewCustomer("c1", "Someone Else"));

        var error = await Assert.ThrowsAsync<ConflictException>(() => session.UnitOfWork.SaveChangesAsync());
        Assert.Contains("customer number", error.Message);
    }

    [DatabaseFact]
    public async Task TheStoredNameIsNotChangedByAFailedDuplicateInsert()
    {
        await TestEntities.SaveAsync(Database, customer: TestEntities.NewCustomer("C1", "Alice"));

        await using (var session = Database.CreateSession())
        {
            await session.Customers.AddAsync(TestEntities.NewCustomer("C1", "Mallory"));
            await Assert.ThrowsAsync<ConflictException>(() => session.UnitOfWork.SaveChangesAsync());
        }

        await using var reader = Database.CreateSession();
        Assert.Equal("Alice", (await reader.Customers.GetByCustomerNumberAsync("C1"))!.Name);
    }
}
