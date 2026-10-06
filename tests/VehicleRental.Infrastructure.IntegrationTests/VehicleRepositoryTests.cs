using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

public class VehicleRepositoryTests : DatabaseTestBase
{
    public VehicleRepositoryTests(PostgresFixture database) : base(database)
    {
    }

    [DatabaseFact]
    public async Task AddedVehicle_CanBeReloadedByRegistrationNumber_WithAllValues()
    {
        var vehicle = TestEntities.NewVehicle("ABC-123", dailyRate: 59.99m, type: VehicleType.Motorcycle);
        await TestEntities.SaveAsync(Database, vehicle: vehicle);

        await using var session = Database.CreateSession();
        var loaded = await session.Vehicles.GetByRegistrationNumberAsync("ABC-123");

        Assert.NotNull(loaded);
        Assert.Equal(vehicle.Id, loaded.Id);
        Assert.Equal("Toyota Corolla", loaded.DisplayName);
        Assert.Equal(2022, loaded.Year);
        Assert.Equal(VehicleType.Motorcycle, loaded.VehicleType);
        Assert.Equal(59.99m, loaded.DailyRate);
        Assert.Equal(VehicleAvailabilityStatus.Available, loaded.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task GetByRegistrationNumber_WithUnknownNumber_ReturnsNull()
    {
        await using var session = Database.CreateSession();

        Assert.Null(await session.Vehicles.GetByRegistrationNumberAsync("NOPE-1"));
    }

    [DatabaseFact]
    public async Task AddingTheSameRegistrationNumberTwice_IsRejectedAsAConflict()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));

        await using var session = Database.CreateSession();
        await session.Vehicles.AddAsync(TestEntities.NewVehicle("abc-123"));

        var error = await Assert.ThrowsAsync<ConflictException>(() => session.UnitOfWork.SaveChangesAsync());
        Assert.Contains("registration number", error.Message);
    }

    [DatabaseFact]
    public async Task GetAll_ReturnsVehiclesOrderedByRegistrationNumber()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("GHI-789"));
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("DEF-456"));

        await using var session = Database.CreateSession();
        var all = await session.Vehicles.GetAllAsync();

        Assert.Equal(new[] { "ABC-123", "DEF-456", "GHI-789" }, all.Select(v => v.RegistrationNumber));
    }

    private async Task SeedFleetAsync()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("CAR-001", 60m, VehicleType.Car));
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("CAR-002", 75m, VehicleType.Car));
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("BIK-001", 40m, VehicleType.Motorcycle));
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("VAN-001", 90m, VehicleType.Van));
    }

    [DatabaseFact]
    public async Task Search_ByType_ReturnsOnlyThatType()
    {
        await SeedFleetAsync();

        await using var session = Database.CreateSession();
        var cars = await session.Vehicles.SearchAsync(new VehicleSearchCriteria(VehicleType: VehicleType.Car));

        Assert.Equal(new[] { "CAR-001", "CAR-002" }, cars.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task Search_ByMaximumRate_IncludesTheBoundary()
    {
        await SeedFleetAsync();

        await using var session = Database.CreateSession();
        var affordable = await session.Vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: 60m));

        Assert.Equal(new[] { "BIK-001", "CAR-001" }, affordable.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task Search_ByTypeAndRate_AppliesBothFilters()
    {
        await SeedFleetAsync();

        await using var session = Database.CreateSession();
        var result = await session.Vehicles.SearchAsync(
            new VehicleSearchCriteria(VehicleType.Car, MaximumDailyRate: 70m));

        Assert.Equal(new[] { "CAR-001" }, result.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task Search_ByAvailability_ExcludesRentedVehicles()
    {
        await SeedFleetAsync();
        await using (var session = Database.CreateSession())
        {
            var vehicle = await session.Vehicles.GetByRegistrationNumberAsync("CAR-001");
            vehicle!.MarkAsRented();
            await session.UnitOfWork.SaveChangesAsync();
        }

        await using var reader = Database.CreateSession();
        var available = await reader.Vehicles.SearchAsync(
            new VehicleSearchCriteria(Availability: VehicleAvailabilityStatus.Available));
        var rented = await reader.Vehicles.SearchAsync(
            new VehicleSearchCriteria(Availability: VehicleAvailabilityStatus.Rented));

        Assert.DoesNotContain(available, v => v.RegistrationNumber == "CAR-001");
        Assert.Equal(3, available.Count);
        Assert.Equal(new[] { "CAR-001" }, rented.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task Search_WithNoFilters_ReturnsEveryVehicle()
    {
        await SeedFleetAsync();

        await using var session = Database.CreateSession();

        Assert.Equal(4, (await session.Vehicles.SearchAsync(new VehicleSearchCriteria())).Count);
    }

    [DatabaseFact]
    public async Task Search_WithNoMatches_ReturnsEmptyList()
    {
        await SeedFleetAsync();

        await using var session = Database.CreateSession();

        Assert.Empty(await session.Vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: 1m)));
    }

    [DatabaseFact]
    public async Task ChangingAvailability_IsSavedAndVisibleToANewSession()
    {
        await TestEntities.SaveAsync(Database, vehicle: TestEntities.NewVehicle("ABC-123"));

        await using (var writer = Database.CreateSession())
        {
            var vehicle = await writer.Vehicles.GetByRegistrationNumberAsync("ABC-123");
            vehicle!.MarkAsRented();
            await writer.UnitOfWork.SaveChangesAsync();
        }

        await using var reader = Database.CreateSession();
        var reloaded = await reader.Vehicles.GetByRegistrationNumberAsync("ABC-123");

        Assert.Equal(VehicleAvailabilityStatus.Rented, reloaded!.AvailabilityStatus);
    }
}
