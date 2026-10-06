using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The Application services running against the real database, each call in a fresh session,
/// the way separate runs of the application would use it.
/// </summary>
public class RentalWorkflowTests : DatabaseTestBase
{
    public RentalWorkflowTests(PostgresFixture database) : base(database)
    {
    }

    private async Task<T> WithServicesAsync<T>(Func<VehicleService, RentalService, CustomerService, Task<T>> action)
    {
        await using var session = Database.CreateSession();

        return await action(
            new VehicleService(session.Vehicles, session.UnitOfWork),
            new RentalService(session.Vehicles, session.Customers, session.Rentals, session.UnitOfWork),
            new CustomerService(session.Customers, session.UnitOfWork));
    }

    private Task AddVehicleAsync(string registration, decimal rate = 100m) =>
        WithServicesAsync(async (vehicles, _, _) =>
            await vehicles.AddVehicleAsync(
                new AddVehicleRequest(registration, "Toyota", "Corolla", 2022, VehicleType.Car, rate)));

    private Task<RentalDto> RentAsync(string registration, string customerNumber, string name, int days, bool promo = false) =>
        WithServicesAsync((_, rentals, _) =>
            rentals.StartRentalAsync(new StartRentalRequest(registration, customerNumber, name, days, promo)));

    [DatabaseFact]
    public async Task ARentalStartedInOneSession_IsStillThereInTheNext()
    {
        await AddVehicleAsync("ABC-123");

        var started = await RentAsync("ABC-123", "C1", "Alice", days: 3);

        var history = await WithServicesAsync((_, rentals, _) => rentals.GetRentalHistoryAsync());
        var stored = Assert.Single(history);
        Assert.Equal(started.Id, stored.Id);
        Assert.Equal(300m, stored.TotalCost);
        Assert.Equal(RentalStatus.Active, stored.Status);

        var vehicle = await WithServicesAsync((vehicles, _, _) => vehicles.GetByRegistrationNumberAsync("ABC-123"));
        Assert.Equal(VehicleAvailabilityStatus.Rented, vehicle.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task RentingAnAlreadyRentedVehicle_IsRefused()
    {
        await AddVehicleAsync("ABC-123");
        await RentAsync("ABC-123", "C1", "Alice", days: 3);

        await Assert.ThrowsAsync<ConflictException>(() => RentAsync("ABC-123", "C2", "Bob", days: 2));
    }

    [DatabaseFact]
    public async Task ReturningAVehicle_CompletesTheRentalAndFreesTheVehicle()
    {
        await AddVehicleAsync("ABC-123");
        await RentAsync("ABC-123", "C1", "Alice", days: 3);

        var returned = await WithServicesAsync((_, rentals, _) => rentals.ReturnVehicleAsync("ABC-123"));

        Assert.Equal(RentalStatus.Completed, returned.Status);
        Assert.NotNull(returned.ActualReturnDate);

        var history = await WithServicesAsync((_, rentals, _) => rentals.GetRentalHistoryAsync());
        Assert.Equal(RentalStatus.Completed, Assert.Single(history).Status);
        var vehicle = await WithServicesAsync((vehicles, _, _) => vehicles.GetByRegistrationNumberAsync("ABC-123"));
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
        await Assert.ThrowsAsync<ConflictException>(
            () => WithServicesAsync((_, rentals, _) => rentals.ReturnVehicleAsync("ABC-123")));
    }

    [DatabaseFact]
    public async Task ACustomerWhoRentsTwice_IsStoredOnce()
    {
        await AddVehicleAsync("ABC-123");
        await AddVehicleAsync("DEF-456");

        await RentAsync("ABC-123", "C1", "Alice", days: 2);
        await RentAsync("DEF-456", "c1", "alice", days: 2);

        await using var context = Database.CreateContext();
        Assert.Equal(1, await context.Customers.CountAsync());
        Assert.Equal(2, await context.Rentals.CountAsync());
    }

    [DatabaseFact]
    public async Task AKnownCustomerNumberWithADifferentName_IsRefusedAndChangesNothing()
    {
        await AddVehicleAsync("ABC-123");
        await AddVehicleAsync("DEF-456");
        await RentAsync("ABC-123", "C1", "Alice", days: 2);

        await Assert.ThrowsAsync<ConflictException>(() => RentAsync("DEF-456", "C1", "Mallory", days: 2));

        var customer = await WithServicesAsync((_, _, customers) => customers.GetByCustomerNumberAsync("C1"));
        Assert.Equal("Alice", customer.Name);
        var vehicle = await WithServicesAsync((vehicles, _, _) => vehicles.GetByRegistrationNumberAsync("DEF-456"));
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task EarlierRentalTotals_AreUnchanged_WhenTheVehicleIsRentedAgainWithDifferentPricing()
    {
        await AddVehicleAsync("ABC-123", rate: 100m);
        var first = await RentAsync("ABC-123", "C1", "Alice", days: 5);
        await WithServicesAsync((_, rentals, _) => rentals.ReturnVehicleAsync("ABC-123"));

        var second = await RentAsync("ABC-123", "C2", "Bob", days: 10);

        var history = await WithServicesAsync((_, rentals, _) => rentals.GetRentalHistoryAsync());
        Assert.Equal(500m, history.Single(r => r.Id == first.Id).TotalCost);
        Assert.Equal(800m, history.Single(r => r.Id == second.Id).TotalCost);
    }

    [DatabaseFact]
    public async Task AddingAVehicleWithAnExistingRegistrationNumber_IsRefused()
    {
        await AddVehicleAsync("ABC-123");

        await Assert.ThrowsAsync<ConflictException>(() => AddVehicleAsync("abc-123"));
    }

    [DatabaseFact]
    public async Task Search_ThroughTheService_FiltersInTheDatabase()
    {
        await AddVehicleAsync("ABC-123", rate: 60m);
        await AddVehicleAsync("DEF-456", rate: 90m);

        var cheap = await WithServicesAsync((vehicles, _, _) =>
            vehicles.SearchAsync(new VehicleRental.Application.Abstractions.VehicleSearchCriteria(MaximumDailyRate: 70m)));

        Assert.Equal(new[] { "ABC-123" }, cheap.Select(v => v.RegistrationNumber));
    }
}
