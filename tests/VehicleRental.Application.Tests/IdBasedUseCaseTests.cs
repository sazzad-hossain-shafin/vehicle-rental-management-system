using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests;

/// <summary>
/// The use cases the web API relies on: lookups by permanent ID, strict customer creation,
/// and starting and returning rentals by ID.
/// </summary>
public class IdBasedUseCaseTests
{
    private readonly TestApp _app = new();

    // ----- Lookups by ID -----

    [Fact]
    public async Task Vehicle_CanBeFoundByItsId()
    {
        var added = await _app.AddVehicleAsync("V1");

        var found = await _app.Vehicles.GetByIdAsync(added.Id);

        Assert.Equal("V1", found.RegistrationNumber);
    }

    [Fact]
    public async Task Vehicle_WithUnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Vehicles.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Customer_CanBeFoundByItsId()
    {
        var created = await _app.Customers.CreateAsync("C1", "Alice");

        var found = await _app.Customers.GetByIdAsync(created.Id);

        Assert.Equal("Alice", found.Name);
    }

    [Fact]
    public async Task Customer_WithUnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Customers.GetByIdAsync(Guid.NewGuid()));
    }

    // ----- Strict customer creation -----

    [Fact]
    public async Task CreateCustomer_WithNewNumber_StoresTheCustomer()
    {
        var created = await _app.Customers.CreateAsync("c-100", "  Alice ");

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("C-100", created.CustomerNumber);
        Assert.Equal("Alice", created.Name);
        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Theory]
    [InlineData("Alice")]
    [InlineData("Someone Else")]
    public async Task CreateCustomer_WithANumberAlreadyInUse_ThrowsConflictException_EvenWithTheSameName(string name)
    {
        await _app.Customers.CreateAsync("C1", "Alice");

        await Assert.ThrowsAsync<ConflictException>(() => _app.Customers.CreateAsync("c1", name));

        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Theory]
    [InlineData("", "Alice")]
    [InlineData("C1", "  ")]
    public async Task CreateCustomer_WithEmptyDetails_ThrowsArgumentException(string number, string name)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _app.Customers.CreateAsync(number, name));
    }

    // ----- Starting a rental by IDs -----

    private async Task<(Guid VehicleId, Guid CustomerId)> SetUpAsync(decimal rate = 100m)
    {
        var vehicle = await _app.AddVehicleAsync("V1", rate);
        var customer = await _app.Customers.CreateAsync("C1", "Alice");

        return (vehicle.Id, customer.Id);
    }

    [Fact]
    public async Task StartRentalById_WithExistingVehicleAndCustomer_StartsAnActiveRental()
    {
        var (vehicleId, customerId) = await SetUpAsync();

        var rental = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 3));

        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Equal(300m, rental.TotalCost);
        Assert.Equal(vehicleId, rental.VehicleId);
        Assert.Equal(customerId, rental.CustomerId);
        Assert.Equal(1, _app.CustomerRepository.Count); // no extra customer is created
    }

    [Theory]
    [InlineData(3, false, 300)]
    [InlineData(3, true, 270)]
    [InlineData(7, true, 560)]
    public async Task StartRentalById_AppliesThePricingPolicy(int days, bool promotion, int expectedTotal)
    {
        var (vehicleId, customerId) = await SetUpAsync();

        var rental = await _app.Rentals.StartRentalAsync(
            new StartRentalByIdRequest(vehicleId, customerId, days, promotion));

        Assert.Equal((decimal)expectedTotal, rental.TotalCost);
    }

    [Fact]
    public async Task StartRentalById_WithUnknownVehicle_ThrowsNotFoundException()
    {
        var (_, customerId) = await SetUpAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(Guid.NewGuid(), customerId, 3)));
    }

    [Fact]
    public async Task StartRentalById_WithUnknownCustomer_ThrowsNotFoundExceptionAndLeavesVehicleAvailable()
    {
        var (vehicleId, _) = await SetUpAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, Guid.NewGuid(), 3)));

        var vehicle = await _app.Vehicles.GetByIdAsync(vehicleId);
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [Fact]
    public async Task StartRentalById_WhenVehicleIsRented_ThrowsConflictException()
    {
        var (vehicleId, customerId) = await SetUpAsync();
        await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 3));

        await Assert.ThrowsAsync<ConflictException>(
            () => _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 2)));
    }

    [Fact]
    public async Task StartRentalById_WithZeroDays_ThrowsArgumentException()
    {
        var (vehicleId, customerId) = await SetUpAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 0)));
    }

    // ----- Get and return a rental by ID -----

    [Fact]
    public async Task GetRental_ReturnsTheRentalWithItsPriceSnapshot()
    {
        var (vehicleId, customerId) = await SetUpAsync();
        var started = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 5));

        var found = await _app.Rentals.GetRentalAsync(started.Id);

        Assert.Equal(started, found);
    }

    [Fact]
    public async Task GetRental_WithUnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Rentals.GetRentalAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReturnRental_CompletesItAndFreesTheVehicle()
    {
        var (vehicleId, customerId) = await SetUpAsync();
        var started = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 3));
        _app.Clock.AdvanceDays(2);

        var completed = await _app.Rentals.ReturnRentalAsync(started.Id);

        Assert.Equal(RentalStatus.Completed, completed.Status);
        Assert.Equal(TestApp.Today.AddDays(2), completed.ActualReturnDate);
        Assert.Equal(started.TotalCost, completed.TotalCost);
        Assert.Equal(VehicleAvailabilityStatus.Available, (await _app.Vehicles.GetByIdAsync(vehicleId)).AvailabilityStatus);
    }

    [Fact]
    public async Task ReturnRental_Twice_ThrowsConflictException()
    {
        var (vehicleId, customerId) = await SetUpAsync();
        var started = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 3));
        await _app.Rentals.ReturnRentalAsync(started.Id);

        await Assert.ThrowsAsync<ConflictException>(() => _app.Rentals.ReturnRentalAsync(started.Id));
    }

    [Fact]
    public async Task ReturnRental_WithUnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Rentals.ReturnRentalAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReturningOneRental_DoesNotChangeTheTotalOfAnEarlierOne()
    {
        var (vehicleId, customerId) = await SetUpAsync();
        var first = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 5));
        await _app.Rentals.ReturnRentalAsync(first.Id);
        var second = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, 10));

        Assert.Equal(500m, (await _app.Rentals.GetRentalAsync(first.Id)).TotalCost);
        Assert.Equal(800m, (await _app.Rentals.GetRentalAsync(second.Id)).TotalCost);
    }
}
