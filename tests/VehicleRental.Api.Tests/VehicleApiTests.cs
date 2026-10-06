using System.Net;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Api.Tests;

public class VehicleApiTests : ApiTestBase
{
    public VehicleApiTests(PostgresApiFixture api) : base(api)
    {
    }

    // ----- Create -----

    [DatabaseFact]
    public async Task PostVehicle_WithValidData_Returns201WithLocationAndTheNewVehicle()
    {
        var response = await Client.PostJsonAsync("/api/v1/vehicles", new
        {
            registrationNumber = "abc-123",
            make = "Toyota",
            model = "Corolla",
            year = 2022,
            vehicleType = "Car",
            dailyRate = 59.99m
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var vehicle = await response.ReadAsync<VehicleDto>();
        Assert.NotEqual(Guid.Empty, vehicle.Id);
        Assert.Equal("ABC-123", vehicle.RegistrationNumber);
        Assert.Equal(VehicleType.Car, vehicle.VehicleType);
        Assert.Equal(59.99m, vehicle.DailyRate);
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
        Assert.EndsWith($"/api/v1/vehicles/{vehicle.Id}", response.Headers.Location!.ToString());
    }

    [DatabaseFact]
    public async Task PostVehicle_IgnoresAnyIdOrAvailabilitySentByTheClient()
    {
        var sentId = Guid.NewGuid();

        var response = await Client.PostJsonAsync("/api/v1/vehicles", new
        {
            id = sentId,
            availabilityStatus = "Rented",
            registrationNumber = "ABC-123",
            make = "Toyota",
            model = "Corolla",
            year = 2022,
            vehicleType = "Car",
            dailyRate = 60
        });

        var vehicle = await response.ReadAsync<VehicleDto>();
        Assert.NotEqual(sentId, vehicle.Id);
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task PostVehicle_WithADuplicateRegistration_Returns409ProblemDetails()
    {
        await Client.CreateVehicleAsync("ABC-123");

        var response = await Client.PostJsonAsync("/api/v1/vehicles", new
        {
            registrationNumber = "abc-123",
            make = "Honda",
            model = "Civic",
            year = 2020,
            vehicleType = "Car",
            dailyRate = 50
        });

        var problem = await response.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Contains("ABC-123", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task PostVehicle_BreakingADomainRule_Returns400ProblemDetails()
    {
        var response = await Client.PostJsonAsync("/api/v1/vehicles", new
        {
            registrationNumber = "ABC-123",
            make = "Toyota",
            model = "Corolla",
            year = 2022,
            vehicleType = "Car",
            dailyRate = -5
        });

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("greater than", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task PostVehicle_WithAnInvalidRegistrationNumber_Returns400ProblemDetails()
    {
        var response = await Client.PostJsonAsync("/api/v1/vehicles", new
        {
            registrationNumber = "!!",
            make = "Toyota",
            model = "Corolla",
            year = 2022,
            vehicleType = "Car",
            dailyRate = 50
        });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    // ----- Get -----

    [DatabaseFact]
    public async Task GetVehicleById_ReturnsTheVehicle()
    {
        var created = await Client.CreateVehicleAsync("ABC-123");

        var response = await Client.GetAsync($"/api/v1/vehicles/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, (await response.ReadAsync<VehicleDto>()).Id);
    }

    [DatabaseFact]
    public async Task GetVehicleById_WithAnUnknownId_Returns404ProblemDetails()
    {
        var response = await Client.GetAsync($"/api/v1/vehicles/{Guid.NewGuid()}");

        await response.AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task GetVehicleByRegistration_IsCaseInsensitive()
    {
        var created = await Client.CreateVehicleAsync("ABC-123");

        var response = await Client.GetAsync("/api/v1/vehicles/by-registration/abc-123");

        Assert.Equal(created.Id, (await response.ReadAsync<VehicleDto>()).Id);
    }

    [DatabaseFact]
    public async Task GetVehicleByRegistration_WithAnUnknownNumber_Returns404ProblemDetails()
    {
        var response = await Client.GetAsync("/api/v1/vehicles/by-registration/NOPE-1");

        await response.AssertProblemAsync(HttpStatusCode.NotFound);
    }

    // ----- List, filter, page -----

    private async Task AddFleetAsync()
    {
        await Client.CreateVehicleAsync("CAR-001", 60m, "Car");
        await Client.CreateVehicleAsync("CAR-002", 75m, "Car");
        await Client.CreateVehicleAsync("BIK-001", 40m, "Motorcycle");
        await Client.CreateVehicleAsync("VAN-001", 90m, "Van");
    }

    [DatabaseFact]
    public async Task GetVehicles_WithNoVehicles_ReturnsAnEmptyPage()
    {
        var page = await (await Client.GetAsync("/api/v1/vehicles")).ReadAsync<PagedResult<VehicleDto>>();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(Paging.DefaultPageSize, page.PageSize);
    }

    [DatabaseFact]
    public async Task GetVehicles_ReturnsEveryVehicleOrderedByRegistration()
    {
        await AddFleetAsync();

        var page = await (await Client.GetAsync("/api/v1/vehicles")).ReadAsync<PagedResult<VehicleDto>>();

        Assert.Equal(new[] { "BIK-001", "CAR-001", "CAR-002", "VAN-001" }, page.Items.Select(v => v.RegistrationNumber));
        Assert.Equal(4, page.TotalCount);
    }

    [DatabaseFact]
    public async Task GetVehicles_FiltersByType_IgnoringLetterCase()
    {
        await AddFleetAsync();

        var page = await (await Client.GetAsync("/api/v1/vehicles?vehicleType=car")).ReadAsync<PagedResult<VehicleDto>>();

        Assert.Equal(new[] { "CAR-001", "CAR-002" }, page.Items.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task GetVehicles_FiltersByMaximumDailyRate_IncludingTheBoundary()
    {
        await AddFleetAsync();

        var page = await (await Client.GetAsync("/api/v1/vehicles?maxDailyRate=60")).ReadAsync<PagedResult<VehicleDto>>();

        Assert.Equal(new[] { "BIK-001", "CAR-001" }, page.Items.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task GetVehicles_FiltersByAvailability_AfterARentalStarts()
    {
        await AddFleetAsync();
        var vehicle = await Client.GetAsync("/api/v1/vehicles/by-registration/CAR-001");
        var customer = await Client.CreateCustomerAsync();
        await Client.StartRentalAsync((await vehicle.ReadAsync<VehicleDto>()).Id, customer.Id);

        var available = await (await Client.GetAsync("/api/v1/vehicles?availability=Available"))
            .ReadAsync<PagedResult<VehicleDto>>();
        var rented = await (await Client.GetAsync("/api/v1/vehicles?availability=rented"))
            .ReadAsync<PagedResult<VehicleDto>>();

        Assert.Equal(3, available.TotalCount);
        Assert.DoesNotContain(available.Items, v => v.RegistrationNumber == "CAR-001");
        Assert.Equal(new[] { "CAR-001" }, rented.Items.Select(v => v.RegistrationNumber));
    }

    [DatabaseFact]
    public async Task GetVehicles_CombinesFilters()
    {
        await AddFleetAsync();

        var page = await (await Client.GetAsync("/api/v1/vehicles?vehicleType=Car&maxDailyRate=70"))
            .ReadAsync<PagedResult<VehicleDto>>();

        Assert.Equal(new[] { "CAR-001" }, page.Items.Select(v => v.RegistrationNumber));
        Assert.Equal(1, page.TotalCount);
    }

    [DatabaseFact]
    public async Task GetVehicles_Paging_ReturnsTheRequestedSliceAndTotals()
    {
        await AddFleetAsync();

        var page = await (await Client.GetAsync("/api/v1/vehicles?page=2&pageSize=3")).ReadAsync<PagedResult<VehicleDto>>();

        Assert.Equal(new[] { "VAN-001" }, page.Items.Select(v => v.RegistrationNumber));
        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.PageSize);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }

    [DatabaseFact]
    public async Task GetVehicles_PageBeyondTheEnd_IsEmptyButKeepsTheTotal()
    {
        await AddFleetAsync();

        var page = await (await Client.GetAsync("/api/v1/vehicles?page=9")).ReadAsync<PagedResult<VehicleDto>>();

        Assert.Empty(page.Items);
        Assert.Equal(4, page.TotalCount);
    }

    [DatabaseFact]
    public async Task GetVehicles_ResponsesUseTextForEnums()
    {
        await Client.CreateVehicleAsync("ABC-123", type: "Van");

        string json = await Client.GetStringAsync("/api/v1/vehicles");

        Assert.Contains("\"vehicleType\":\"Van\"", json);
        Assert.Contains("\"availabilityStatus\":\"Available\"", json);
    }
}
