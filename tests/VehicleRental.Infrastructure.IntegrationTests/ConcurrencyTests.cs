using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The guarantee under test: two requests can never both successfully rent (or both complete) the
/// same vehicle. It comes from the database, not from timing: a row version on vehicles and rentals,
/// and a unique index allowing one active rental per vehicle.
/// </summary>
public class ConcurrencyTests : DatabaseTestBase
{
    public ConcurrencyTests(PostgresFixture database) : base(database)
    {
    }

    [DatabaseFact]
    public async Task TwoRequestsThatBothSawTheVehicleAvailable_OnlyTheFirstToSaveWins()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));

        // Both requests load the vehicle while it is still available.
        await using var first = Database.CreateSession();
        await using var second = Database.CreateSession();
        var vehicleSeenByFirst = (await first.Vehicles.GetByRegistrationNumberAsync("ABC-123"))!;
        var vehicleSeenBySecond = (await second.Vehicles.GetByRegistrationNumberAsync("ABC-123"))!;

        var aliceRental = TestEntities.NewRental(vehicleSeenByFirst, TestEntities.NewCustomer("C1", "Alice"));
        var bobRental = TestEntities.NewRental(vehicleSeenBySecond, TestEntities.NewCustomer("C2", "Bob"));

        await first.Customers.AddAsync(aliceRental.Customer);
        await first.Rentals.AddAsync(aliceRental);
        await first.UnitOfWork.SaveChangesAsync();

        await second.Customers.AddAsync(bobRental.Customer);
        await second.Rentals.AddAsync(bobRental);
        await Assert.ThrowsAsync<ConflictException>(() => second.UnitOfWork.SaveChangesAsync());

        await using var context = Database.CreateContext();
        var rentals = await context.Rentals.Include(r => r.Customer).ToListAsync();
        var winner = Assert.Single(rentals);
        Assert.Equal("Alice", winner.Customer.Name);
        Assert.Equal(RentalStatus.Active, winner.Status);
    }

    [DatabaseFact]
    public async Task TheLosingRequest_LeavesNothingBehind_NotEvenItsNewCustomer()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));
        await using var first = Database.CreateSession();
        await using var second = Database.CreateSession();
        var seenByFirst = (await first.Vehicles.GetByRegistrationNumberAsync("ABC-123"))!;
        var seenBySecond = (await second.Vehicles.GetByRegistrationNumberAsync("ABC-123"))!;
        var aliceRental = TestEntities.NewRental(seenByFirst, TestEntities.NewCustomer("C1", "Alice"));
        var bobRental = TestEntities.NewRental(seenBySecond, TestEntities.NewCustomer("C2", "Bob"));

        await first.Customers.AddAsync(aliceRental.Customer);
        await first.Rentals.AddAsync(aliceRental);
        await first.UnitOfWork.SaveChangesAsync();
        await second.Customers.AddAsync(bobRental.Customer);
        await second.Rentals.AddAsync(bobRental);
        await Assert.ThrowsAsync<ConflictException>(() => second.UnitOfWork.SaveChangesAsync());

        await using var reader = Database.CreateSession();
        Assert.Null(await reader.Customers.GetByCustomerNumberAsync("C2"));
    }

    [DatabaseFact]
    public async Task TwoRequestsStartingARentalAtTheSameTime_ExactlyOneSucceeds()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Database.ResetAsync();
            await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));

            Task<Exception?> Rent(string customerNumber, string name) => Task.Run(async () =>
            {
                try
                {
                    await using var session = Database.CreateSession();
                    var service = new RentalService(
                        session.Vehicles, session.Customers, session.Rentals, session.UnitOfWork);
                    await service.StartRentalAsync(new StartRentalRequest("ABC-123", customerNumber, name, 3));

                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var outcomes = await Task.WhenAll(Rent("C1", "Alice"), Rent("C2", "Bob"));

            Assert.Equal(1, outcomes.Count(o => o is null));
            Assert.Single(outcomes, o => o is ConflictException);

            await using var context = Database.CreateContext();
            Assert.Equal(1, await context.Rentals.CountAsync());
            Assert.Equal(1, await context.Customers.CountAsync()); // the loser's new customer was rolled back too
        }
    }

    [DatabaseFact]
    public async Task TwoRequestsCompletingTheSameRental_OnlyTheFirstSucceeds()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123");
        var customer = TestEntities.NewCustomer();
        await TestEntities.SaveAsync(Database, vehicle, customer, TestEntities.NewRental(vehicle, customer));

        await using var first = Database.CreateSession();
        await using var second = Database.CreateSession();
        var rentalSeenByFirst = (await first.Rentals.GetActiveForVehicleAsync(vehicle.Id))!;
        var rentalSeenBySecond = (await second.Rentals.GetActiveForVehicleAsync(vehicle.Id))!;

        rentalSeenByFirst.Complete(TestEntities.Start.AddDays(2));
        await first.UnitOfWork.SaveChangesAsync();

        rentalSeenBySecond.Complete(TestEntities.Start.AddDays(3));
        await Assert.ThrowsAsync<ConflictException>(() => second.UnitOfWork.SaveChangesAsync());

        await using var reader = Database.CreateSession();
        var stored = (await reader.Rentals.GetAllAsync()).Single();
        Assert.Equal(TestEntities.Start.AddDays(2), stored.ActualReturnDate);
    }

    [DatabaseFact]
    public async Task ARequestWorkingFromStaleData_CannotOverwriteALaterChange()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));

        await using var stale = Database.CreateSession();
        var staleVehicle = (await stale.Vehicles.GetByRegistrationNumberAsync("ABC-123"))!;

        await using (var other = Database.CreateSession())
        {
            var vehicle = (await other.Vehicles.GetByRegistrationNumberAsync("ABC-123"))!;
            vehicle.MarkAsRented();
            await other.UnitOfWork.SaveChangesAsync();
        }

        staleVehicle.MarkAsRented(); // valid from where the stale request stands
        await Assert.ThrowsAsync<ConflictException>(() => stale.UnitOfWork.SaveChangesAsync());
    }

    [DatabaseFact]
    public async Task RentalsOfDifferentVehicles_DoNotInterfere()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("DEF-456"));

        async Task Rent(string registration, string customerNumber)
        {
            await using var session = Database.CreateSession();
            var service = new RentalService(session.Vehicles, session.Customers, session.Rentals, session.UnitOfWork);
            await service.StartRentalAsync(new StartRentalRequest(registration, customerNumber, "Person " + customerNumber, 2));
        }

        await Task.WhenAll(Rent("ABC-123", "C1"), Rent("DEF-456", "C2"));

        await using var context = Database.CreateContext();
        Assert.Equal(2, await context.Rentals.CountAsync(r => r.Status == RentalStatus.Active));
    }
}
