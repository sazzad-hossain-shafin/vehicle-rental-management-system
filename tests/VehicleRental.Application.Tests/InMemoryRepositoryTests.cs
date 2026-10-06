using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Tests.Fakes;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.Application.Tests;

/// <summary>
/// Checks that the fake repositories keep to the contract the services rely on. The real
/// EF Core repositories are tested against PostgreSQL in the integration tests.
/// </summary>
public class InMemoryRepositoryTests
{
    private static Vehicle NewVehicle(string registration = "V1") =>
        new(registration, "Toyota", "Corolla", 2022, VehicleType.Car, 100m);

    [Fact]
    public async Task VehicleRepository_AddingTheSameRegistrationTwice_ThrowsConflictException()
    {
        var repository = new InMemoryVehicleRepository();
        await repository.AddAsync(NewVehicle("V1"));

        await Assert.ThrowsAsync<ConflictException>(() => repository.AddAsync(NewVehicle("v1")));

        Assert.Equal(1, repository.Count);
    }

    [Fact]
    public async Task VehicleRepository_GetAll_ReturnsASnapshotThatLaterAdditionsDoNotChange()
    {
        var repository = new InMemoryVehicleRepository();
        await repository.AddAsync(NewVehicle("V1"));

        var snapshot = await repository.GetAllAsync();
        await repository.AddAsync(NewVehicle("V2"));

        Assert.Single(snapshot);
        Assert.Equal(2, repository.Count);
    }

    [Fact]
    public async Task CustomerRepository_AddingTheSameNumberTwice_ThrowsConflictException()
    {
        var repository = new InMemoryCustomerRepository();
        await repository.AddAsync(new Customer("C1", "Alice"));

        await Assert.ThrowsAsync<ConflictException>(
            () => repository.AddAsync(new Customer("c1", "Someone Else")));

        Assert.Equal(1, repository.Count);
    }

    [Fact]
    public async Task RentalRepository_GetActiveForVehicle_IgnoresCompletedRentals()
    {
        var repository = new InMemoryRentalRepository();
        var vehicle = NewVehicle("V1");
        var start = new DateOnly(2026, 10, 1);
        var rental = Rental.Start(new Customer("C1", "Alice"), vehicle, start, start.AddDays(2), new NormalPricingStrategy());
        await repository.AddAsync(rental);

        Assert.Same(rental, await repository.GetActiveForVehicleAsync(vehicle.Id));

        rental.Complete(start.AddDays(2));

        Assert.Null(await repository.GetActiveForVehicleAsync(vehicle.Id));
        Assert.Same(rental, await repository.GetByIdAsync(rental.Id));
    }

    [Fact]
    public async Task Repositories_WithCancelledToken_ThrowOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new InMemoryVehicleRepository().GetAllAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new InMemoryCustomerRepository().GetByCustomerNumberAsync("C1", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new InMemoryRentalRepository().GetAllAsync(cts.Token));
    }
}
