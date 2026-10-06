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

        Assert.Equal("V1", vehicle.RegistrationNumber);
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

        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("V1");

        Assert.Equal("V1", vehicle.RegistrationNumber);
    }

    [Fact]
    public async Task GetById_IgnoresSurroundingWhitespace()
    {
        await _app.AddVehicleAsync("V1");

        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("  V1 ");

        Assert.Equal("V1", vehicle.RegistrationNumber);
    }

    [Fact]
    public async Task GetByRegistrationNumber_IsCaseInsensitive()
    {
        await _app.AddVehicleAsync("abc-123");

        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("Abc-123");

        Assert.Equal("ABC-123", vehicle.RegistrationNumber);
    }

    [Fact]
    public async Task AddVehicle_AssignsAnInternalIdentifierSeparateFromTheRegistration()
    {
        var first = await _app.AddVehicleAsync("V1");
        var second = await _app.AddVehicleAsync("V2");

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Vehicles.GetByRegistrationNumberAsync("missing"));
    }

    [Fact]
    public async Task GetAll_ReturnsEveryVehicleOrderedByRegistrationNumber()
    {
        await _app.AddVehicleAsync("V2");
        await _app.AddVehicleAsync("V1");

        var vehicles = await _app.Vehicles.GetAllAsync();

        Assert.Equal(new[] { "V1", "V2" }, vehicles.Select(v => v.RegistrationNumber));
    }

    // ----- Search and filtering -----

    private async Task AddFleetAsync()
    {
        await _app.AddVehicleAsync("V1", 60m, VehicleType.Car);
        await _app.AddVehicleAsync("V2", 40m, VehicleType.Motorcycle);
        await _app.AddVehicleAsync("V3", 90m, VehicleType.Van);
        await _app.AddVehicleAsync("V4", 75m, VehicleType.Car);
    }

    [Fact]
    public async Task Search_ByType_ReturnsOnlyThatType()
    {
        await AddFleetAsync();

        var cars = await _app.Vehicles.SearchAsync(new VehicleSearchCriteria(VehicleType: VehicleType.Car));

        Assert.Equal(new[] { "V1", "V4" }, cars.Select(v => v.RegistrationNumber));
    }

    [Fact]
    public async Task Search_ByMaximumRate_IncludesVehiclesAtExactlyThatRate()
    {
        await AddFleetAsync();

        var affordable = await _app.Vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: 60m));

        Assert.Equal(new[] { "V1", "V2" }, affordable.Select(v => v.RegistrationNumber));
    }

    [Fact]
    public async Task Search_ByTypeAndMaximumRate_AppliesBothFilters()
    {
        await AddFleetAsync();

        var result = await _app.Vehicles.SearchAsync(
            new VehicleSearchCriteria(VehicleType.Car, MaximumDailyRate: 70m));

        Assert.Equal(new[] { "V1" }, result.Select(v => v.RegistrationNumber));
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
        await _app.StartRentalAsync(vehicleId: "V1");

        var available = await _app.Vehicles.SearchAsync(
            new VehicleSearchCriteria(Availability: VehicleAvailabilityStatus.Available));

        Assert.Equal(new[] { "V2", "V3", "V4" }, available.Select(v => v.RegistrationNumber));
    }

    [Fact]
    public async Task Search_WithNegativeMaximumRate_ThrowsArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _app.Vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: -1m)));
    }
}
