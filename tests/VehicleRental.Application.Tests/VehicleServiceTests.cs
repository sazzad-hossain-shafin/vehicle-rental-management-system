using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests;

public class VehicleServiceTests
{
    private readonly TestApp _app = new();

    // ----- Adding -----

    [Fact]
    public async Task AddVehicle_WithValidDetails_ReturnsAvailableVehicleAndStoresIt()
    {
        var vehicle = await _app.Vehicles.AddVehicleAsync(
            new AddVehicleRequest("V1", "Honda", "CB500", 2021, VehicleType.Motorcycle, 40m));

        Assert.Equal("V1", vehicle.Id);
        Assert.Equal("Honda CB500", vehicle.DisplayName);
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
        Assert.Equal(1, _app.VehicleRepository.Count);
    }

    [Fact]
    public async Task AddVehicle_WithDuplicateId_ThrowsConflictException()
    {
        await _app.AddVehicleAsync("V1");

        await Assert.ThrowsAsync<ConflictException>(() => _app.AddVehicleAsync("V1"));

        Assert.Equal(1, _app.VehicleRepository.Count);
    }

    [Fact]
    public async Task AddVehicle_WithDuplicateIdInDifferentCase_ThrowsConflictException()
    {
        await _app.AddVehicleAsync("abc-1");

        await Assert.ThrowsAsync<ConflictException>(() => _app.AddVehicleAsync("ABC-1"));
    }

    [Fact]
    public async Task AddVehicle_WithInvalidDailyRate_ThrowsAndStoresNothing()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _app.AddVehicleAsync(dailyRate: 0m));

        Assert.Equal(0, _app.VehicleRepository.Count);
    }

    [Fact]
    public async Task AddVehicle_WithNullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _app.Vehicles.AddVehicleAsync(null!));
    }

    // ----- Retrieval -----

    [Fact]
    public async Task GetById_WithExistingId_ReturnsVehicle()
    {
        await _app.AddVehicleAsync("V1");

        var vehicle = await _app.Vehicles.GetByIdAsync("V1");

        Assert.Equal("V1", vehicle.Id);
    }

    [Fact]
    public async Task GetById_IgnoresSurroundingWhitespace()
    {
        await _app.AddVehicleAsync("V1");

        var vehicle = await _app.Vehicles.GetByIdAsync("  V1 ");

        Assert.Equal("V1", vehicle.Id);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Vehicles.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task GetAll_ReturnsEveryVehicleInTheOrderAdded()
    {
        await _app.AddVehicleAsync("V2");
        await _app.AddVehicleAsync("V1");

        var vehicles = await _app.Vehicles.GetAllAsync();

        Assert.Equal(new[] { "V2", "V1" }, vehicles.Select(v => v.Id));
    }

    // ----- Search and filtering -----

    private async Task AddFleetAsync()
    {
        await _app.AddVehicleAsync("1", 60m, VehicleType.Car);
        await _app.AddVehicleAsync("2", 40m, VehicleType.Motorcycle);
        await _app.AddVehicleAsync("3", 90m, VehicleType.Van);
        await _app.AddVehicleAsync("4", 75m, VehicleType.Car);
    }

    [Fact]
    public async Task Search_ByType_ReturnsOnlyThatType()
    {
        await AddFleetAsync();

        var cars = await _app.Vehicles.SearchAsync(new VehicleSearchCriteria(VehicleType: VehicleType.Car));

        Assert.Equal(new[] { "1", "4" }, cars.Select(v => v.Id));
    }

    [Fact]
    public async Task Search_ByMaximumRate_IncludesVehiclesAtExactlyThatRate()
    {
        await AddFleetAsync();

        var affordable = await _app.Vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: 60m));

        Assert.Equal(new[] { "1", "2" }, affordable.Select(v => v.Id));
    }

    [Fact]
    public async Task Search_ByTypeAndMaximumRate_AppliesBothFilters()
    {
        await AddFleetAsync();

        var result = await _app.Vehicles.SearchAsync(
            new VehicleSearchCriteria(VehicleType.Car, MaximumDailyRate: 70m));

        Assert.Equal(new[] { "1" }, result.Select(v => v.Id));
    }

    [Fact]
    public async Task Search_WithNoMatches_ReturnsEmptyList()
    {
        await AddFleetAsync();

        var result = await _app.Vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: 10m));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Search_WithNoFilters_ReturnsEverything()
    {
        await AddFleetAsync();

        var result = await _app.Vehicles.SearchAsync(new VehicleSearchCriteria());

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public async Task Search_ByAvailability_ExcludesRentedVehicles()
    {
        await AddFleetAsync();
        await _app.StartRentalAsync(vehicleId: "1");

        var available = await _app.Vehicles.SearchAsync(
            new VehicleSearchCriteria(Availability: VehicleAvailabilityStatus.Available));

        Assert.Equal(new[] { "2", "3", "4" }, available.Select(v => v.Id));
    }

    [Fact]
    public async Task Search_WithNegativeMaximumRate_ThrowsArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _app.Vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: -1m)));
    }
}
