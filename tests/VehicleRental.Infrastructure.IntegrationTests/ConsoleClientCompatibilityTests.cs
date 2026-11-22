using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// CHARACTERISATION of the Console client's writes today, through the exact persistence path the Console uses
/// (<see cref="PersistenceSession"/> plus the same services its menu builds), connecting as the restricted runtime role
/// on a database with no Row-Level Security, as in production.
/// <para>
/// The Console binds no tenant and has no tenant interceptors, so it writes vehicles and rentals with a NULL CompanyId and
/// no company-customer link, even when companies exist. That is correct for now (tenancy is off), and it is exactly the
/// write gap Phase 14A-5 must close: see docs/multi-tenancy/console-client-strategy.md. When 14A-5 adds the write-gap guard,
/// these tests are expected to change, deliberately and in that pull request, never silently.
/// </para>
/// </summary>
public class ConsoleClientCompatibilityTests : IClassFixture<ConsoleClientCompatibilityTests.ProductionLikeDatabase>
{
    public sealed class ProductionLikeDatabase : TenantTestDatabase
    {
        public ProductionLikeDatabase() => PreviewRls = false;
    }

    private readonly ProductionLikeDatabase _db;

    public ConsoleClientCompatibilityTests(ProductionLikeDatabase db) => _db = db;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    [DatabaseFact]
    public async Task The_console_path_works_as_the_restricted_runtime_role_and_writes_no_company_even_when_companies_exist()
    {
        // Two active companies: today nothing decides which one a Console write belongs to.
        await _db.NewCompanyAsync();
        await _db.NewCompanyAsync();

        string registration = Unique("CON");
        string customerNumber = Unique("CC");
        RentalDto rental;

        await using (PersistenceSession session = PersistenceSession.Create(_db.AppConnectionString()))
        {
            // The same services the Console's menu builds (Program.CreateServices).
            var vehicles = new VehicleService(session.Vehicles, session.UnitOfWork);
            var rentals = new RentalService(session.Vehicles, session.Customers, session.Rentals, session.Reservations, session.UnitOfWork);

            await vehicles.AddVehicleAsync(new AddVehicleRequest(registration, "Toyota", "Corolla", 2022, VehicleType.Car, 60m));
            rental = await rentals.StartRentalAsync(new StartRentalRequest(registration, customerNumber, "Console Customer", 2));
        }

        await using (PersistenceSession session = PersistenceSession.Create(_db.AppConnectionString()))
        {
            var rentals = new RentalService(session.Vehicles, session.Customers, session.Rentals, session.Reservations, session.UnitOfWork);
            Assert.Equal(RentalStatus.Completed, (await rentals.ReturnRentalAsync(rental.Id)).Status);
        }

        Assert.Equal(1, await _db.OwnerCountAsync(
            "SELECT count(*) FROM \"Vehicles\" WHERE \"RegistrationNumber\" = @r AND \"CompanyId\" IS NULL", ("r", registration)));

        Assert.Equal(1, await _db.OwnerCountAsync(
            "SELECT count(*) FROM \"Rentals\" WHERE \"Id\" = @id AND \"CompanyId\" IS NULL AND \"CompanyCustomerId\" IS NULL", ("id", rental.Id)));

        // The customer is created globally, with no link to any company.
        Assert.Equal(1, await _db.OwnerCountAsync(
            "SELECT count(*) FROM \"Customers\" WHERE \"CustomerNumber\" = @n", ("n", customerNumber)));
        Assert.Equal(0, await _db.OwnerCountAsync(
            "SELECT count(*) FROM \"CompanyCustomers\" cc JOIN \"Customers\" c ON c.\"Id\" = cc.\"CustomerId\" WHERE c.\"CustomerNumber\" = @n",
            ("n", customerNumber)));
    }

    [DatabaseFact]
    public async Task The_console_reads_every_vehicle_regardless_of_company_today()
    {
        // Rows that already belong to a company (as after a backfill) are still visible to the Console: with no tenant and
        // no Row-Level Security nothing filters them. After 14A-7 a tenant-less read must find nothing instead.
        Guid company = await _db.NewCompanyAsync();
        string registration = Unique("OWN");

        await using (PersistenceSession session = PersistenceSession.Create(_db.AppConnectionString()))
        {
            await new VehicleService(session.Vehicles, session.UnitOfWork)
                .AddVehicleAsync(new AddVehicleRequest(registration, "Ford", "Focus", 2021, VehicleType.Car, 55m));
        }

        await _db.OwnerExecuteAsync(
            "UPDATE \"Vehicles\" SET \"CompanyId\" = @c WHERE \"RegistrationNumber\" = @r", ("c", company), ("r", registration));

        await using (PersistenceSession session = PersistenceSession.Create(_db.AppConnectionString()))
        {
            VehicleDto found = await new VehicleService(session.Vehicles, session.UnitOfWork).GetByRegistrationNumberAsync(registration);
            Assert.Equal(registration, found.RegistrationNumber);
        }
    }
}
