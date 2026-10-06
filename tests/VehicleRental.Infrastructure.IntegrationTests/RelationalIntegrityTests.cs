using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

public class RelationalIntegrityTests : DatabaseTestBase
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";

    public RelationalIntegrityTests(PostgresFixture database) : base(database)
    {
    }

    private async Task<(Guid VehicleId, Guid CustomerId, Guid RentalId)> SaveRentalAsync()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123");
        var customer = TestEntities.NewCustomer();
        var rental = TestEntities.NewRental(vehicle, customer);
        await TestEntities.SaveAsync(Database, vehicle, customer, rental);

        return (vehicle.Id, customer.Id, rental.Id);
    }

    [DatabaseFact]
    public async Task DeletingAVehicleThatHasRentals_IsRefused_AndTheHistoryRemains()
    {
        var (vehicleId, _, rentalId) = await SaveRentalAsync();

        await using (var context = Database.CreateContext())
        {
            var vehicle = await context.Vehicles.SingleAsync(v => v.Id == vehicleId);
            context.Vehicles.Remove(vehicle);

            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)error.InnerException!).SqlState);
        }

        await using var verify = Database.CreateContext();
        Assert.True(await verify.Rentals.AnyAsync(r => r.Id == rentalId));
        Assert.True(await verify.Vehicles.AnyAsync(v => v.Id == vehicleId));
    }

    [DatabaseFact]
    public async Task DeletingACustomerThatHasRentals_IsRefused_AndTheHistoryRemains()
    {
        var (_, customerId, rentalId) = await SaveRentalAsync();

        await using (var context = Database.CreateContext())
        {
            var customer = await context.Customers.SingleAsync(c => c.Id == customerId);
            context.Customers.Remove(customer);

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using var verify = Database.CreateContext();
        Assert.True(await verify.Rentals.AnyAsync(r => r.Id == rentalId));
        Assert.True(await verify.Customers.AnyAsync(c => c.Id == customerId));
    }

    [DatabaseFact]
    public async Task ARentalCanStillBeLoadedWithItsRelatedRecords_AfterAnotherVehicleIsDeleted()
    {
        var (_, _, rentalId) = await SaveRentalAsync();
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("DEF-456"));

        await using (var context = Database.CreateContext())
        {
            context.Vehicles.Remove(await context.Vehicles.SingleAsync(v => v.RegistrationNumber == "DEF-456"));
            await context.SaveChangesAsync(); // allowed: nothing references it
        }

        await using var session = Database.CreateSession();
        var rental = await session.Rentals.GetByIdAsync(rentalId);

        Assert.Equal("ABC-123", rental!.Vehicle.RegistrationNumber);
    }

    [DatabaseFact]
    public async Task TheDatabaseRefusesAVehicleWithANonPositiveRate()
    {
        await using var context = Database.CreateContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Vehicles" ("Id","RegistrationNumber","Make","Model","Year","VehicleType","DailyRate","AvailabilityStatus")
                VALUES (gen_random_uuid(),'BAD-001','Toyota','Corolla',2022,'Car',0,'Available')
                """));

        Assert.Equal(CheckViolation, error.SqlState);
    }

    [DatabaseFact]
    public async Task TheDatabaseRefusesACompletedRentalWithoutAReturnDate()
    {
        var (vehicleId, customerId, _) = await SaveRentalAsync();
        await using var context = Database.CreateContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Rentals" ("Id","CustomerId","VehicleId","StartDate","ExpectedReturnDate","ActualReturnDate","Status","DailyRateAtRental","PricingDescription","BillableDays","TotalCost")
                VALUES (gen_random_uuid(),{customerId},{vehicleId},'2026-01-01','2026-01-03',NULL,'Completed',100,'Normal pricing',2,200)
                """));

        Assert.Equal(CheckViolation, error.SqlState);
    }

    [DatabaseFact]
    public async Task TheDatabaseRefusesASecondActiveRentalForTheSameVehicle()
    {
        var (vehicleId, customerId, _) = await SaveRentalAsync();
        await using var context = Database.CreateContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Rentals" ("Id","CustomerId","VehicleId","StartDate","ExpectedReturnDate","ActualReturnDate","Status","DailyRateAtRental","PricingDescription","BillableDays","TotalCost")
                VALUES (gen_random_uuid(),{customerId},{vehicleId},'2026-02-01','2026-02-03',NULL,'Active',100,'Normal pricing',2,200)
                """));

        Assert.Equal(UniqueViolation, error.SqlState);
    }

    [DatabaseFact]
    public async Task ManyCompletedRentalsForOneVehicle_AreAllowed()
    {
        var (vehicleId, customerId, _) = await SaveRentalAsync();
        await using var context = Database.CreateContext();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Rentals" ("Id","CustomerId","VehicleId","StartDate","ExpectedReturnDate","ActualReturnDate","Status","DailyRateAtRental","PricingDescription","BillableDays","TotalCost")
            VALUES (gen_random_uuid(),{customerId},{vehicleId},'2025-01-01','2025-01-03','2025-01-03','Completed',100,'Normal pricing',2,200),
                   (gen_random_uuid(),{customerId},{vehicleId},'2025-02-01','2025-02-03','2025-02-03','Completed',100,'Normal pricing',2,200)
            """);

        Assert.Equal(3, await context.Rentals.CountAsync(r => EF.Property<Guid>(r, "VehicleId") == vehicleId));
    }
}
