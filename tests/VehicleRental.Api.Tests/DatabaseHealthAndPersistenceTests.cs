using System.Net;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;

namespace VehicleRental.Api.Tests;

public class DatabaseHealthAndPersistenceTests : ApiTestBase
{
    public DatabaseHealthAndPersistenceTests(PostgresApiFixture api) : base(api)
    {
    }

    [DatabaseFact]
    public async Task Health_WithAMigratedDatabase_Returns200Healthy()
    {
        var response = await Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks")[0].GetProperty("status").GetString());
    }

    [DatabaseFact]
    public async Task Health_WhenMigrationsHaveNotBeenApplied_Returns503AndSaysSo()
    {
        string emptyDatabase = await Api.CreateDatabaseAsync(applyMigrations: false);
        await using var factory = new ApiFactory(emptyDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var check = (await response.ReadJsonAsync()).GetProperty("checks")[0];
        Assert.Contains("Apply the migrations", check.GetProperty("description").GetString());
    }

    [DatabaseFact]
    public async Task TheApiNeverAppliesMigrationsOnItsOwn()
    {
        string emptyDatabase = await Api.CreateDatabaseAsync(applyMigrations: false);
        await using var factory = new ApiFactory(emptyDatabase);
        using var client = factory.CreateClient();

        await client.GetAsync("/health"); // the app has started and been used

        // Still no schema: starting the API did not create it.
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Data_SurvivesARestartOfTheApi()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123", 100m);
        var customer = await Client.CreateCustomerAsync("C1", "Alice");
        var rental = await Client.StartRentalAsync(vehicle.Id, customer.Id, 3);

        // A brand-new application instance against the same database stands in for a restart.
        // It uses the same signing key, as a restarted deployment with the same configuration would.
        await using var restarted = new ApiFactory(Api.ConnectionString, signingKey: Api.Factory.SigningKey);
        using var client = restarted.CreateClientWithToken(Client.DefaultRequestHeaders.Authorization!.Parameter!);

        var reloadedVehicle = await (await client.GetAsync($"/api/v1/vehicles/{vehicle.Id}")).ReadAsync<VehicleDto>();
        var reloadedRental = await (await client.GetAsync($"/api/v1/rentals/{rental.Id}")).ReadAsync<RentalDto>();
        var history = await (await client.GetAsync("/api/v1/rentals")).ReadAsync<PagedResult<RentalDto>>();

        Assert.Equal("ABC-123", reloadedVehicle.RegistrationNumber);
        Assert.Equal(rental, reloadedRental);
        Assert.Single(history.Items);
    }
}
