using System.Net;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Api.Tests;

public class RentalApiTests : ApiTestBase
{
    public RentalApiTests(PostgresApiFixture api) : base(api)
    {
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ----- Starting -----

    [DatabaseFact]
    public async Task PostRental_StartsAnActiveRental_Returns201WithLocationAndThePriceSnapshot()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123", 100m);
        var customer = await Client.CreateCustomerAsync("C1", "Alice");

        var response = await Client.StartRentalRawAsync(vehicle.Id, customer.Id, days: 3);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rental = await response.ReadAsync<RentalDto>();
        Assert.EndsWith($"/api/v1/rentals/{rental.Id}", response.Headers.Location!.ToString());
        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Equal(vehicle.Id, rental.VehicleId);
        Assert.Equal(customer.Id, rental.CustomerId);
        Assert.Equal("ABC-123", rental.VehicleRegistrationNumber);
        Assert.Equal("Alice", rental.CustomerName);
        Assert.Equal(Today, rental.StartDate);
        Assert.Equal(Today.AddDays(3), rental.ExpectedReturnDate);
        Assert.Null(rental.ActualReturnDate);
        Assert.Equal(100m, rental.DailyRateAtRental);
        Assert.Equal(3, rental.BillableDays);
        Assert.Equal(300m, rental.TotalCost);
    }

    [DatabaseTheory]
    [InlineData(3, false, 300, "Normal")]
    [InlineData(3, true, 270, "Promotional")]
    [InlineData(6, true, 540, "Promotional")]
    [InlineData(7, false, 560, "Long-term")]
    [InlineData(7, true, 560, "Long-term")]
    [InlineData(10, false, 800, "Long-term")]
    public async Task PostRental_AppliesTheNormalPromotionalOrLongTermPricing(
        int days, bool promotion, int expectedTotal, string expectedPricing)
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123", 100m);
        var customer = await Client.CreateCustomerAsync();

        var rental = await Client.StartRentalAsync(vehicle.Id, customer.Id, days, promotion);

        Assert.Equal((decimal)expectedTotal, rental.TotalCost);
        Assert.StartsWith(expectedPricing, rental.PricingDescription);
    }

    [DatabaseFact]
    public async Task PostRental_MarksTheVehicleAsRented()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var customer = await Client.CreateCustomerAsync();

        await Client.StartRentalAsync(vehicle.Id, customer.Id);

        var reloaded = await (await Client.GetAsync($"/api/v1/vehicles/{vehicle.Id}")).ReadAsync<VehicleDto>();
        Assert.Equal(VehicleAvailabilityStatus.Rented, reloaded.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task PostRental_WithAnUnknownVehicle_Returns404ProblemDetails()
    {
        var customer = await Client.CreateCustomerAsync();

        var response = await Client.StartRentalRawAsync(Guid.NewGuid(), customer.Id);

        var problem = await response.AssertProblemAsync(HttpStatusCode.NotFound);
        Assert.Contains("Vehicle", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task PostRental_WithAnUnknownCustomer_Returns404_AndLeavesTheVehicleAvailable()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");

        var response = await Client.StartRentalRawAsync(vehicle.Id, Guid.NewGuid());

        await response.AssertProblemAsync(HttpStatusCode.NotFound);
        var reloaded = await (await Client.GetAsync($"/api/v1/vehicles/{vehicle.Id}")).ReadAsync<VehicleDto>();
        Assert.Equal(VehicleAvailabilityStatus.Available, reloaded.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task PostRental_ForAVehicleThatIsAlreadyRented_Returns409ProblemDetails()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var alice = await Client.CreateCustomerAsync("C1", "Alice");
        var bob = await Client.CreateCustomerAsync("C2", "Bob");
        await Client.StartRentalAsync(vehicle.Id, alice.Id);

        var response = await Client.StartRentalRawAsync(vehicle.Id, bob.Id);

        var problem = await response.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Contains("not available", problem.GetProperty("detail").GetString());
    }

    [DatabaseTheory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task PostRental_WithNonPositiveDays_Returns400_AndLeavesTheVehicleAvailable(int days)
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var customer = await Client.CreateCustomerAsync();

        var response = await Client.StartRentalRawAsync(vehicle.Id, customer.Id, days);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        var reloaded = await (await Client.GetAsync($"/api/v1/vehicles/{vehicle.Id}")).ReadAsync<VehicleDto>();
        Assert.Equal(VehicleAvailabilityStatus.Available, reloaded.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task TwoSimultaneousRequestsToRentTheSameVehicle_ExactlyOneSucceeds()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var alice = await Client.CreateCustomerAsync("C1", "Alice");
        var bob = await Client.CreateCustomerAsync("C2", "Bob");

        var responses = await Task.WhenAll(
            Client.StartRentalRawAsync(vehicle.Id, alice.Id),
            Client.StartRentalRawAsync(vehicle.Id, bob.Id));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await loser.AssertProblemAsync(HttpStatusCode.Conflict);

        var history = await (await Client.GetAsync("/api/v1/rentals")).ReadAsync<PagedResult<RentalDto>>();
        Assert.Equal(1, history.TotalCount);
    }

    // ----- Reading -----

    [DatabaseFact]
    public async Task GetRentalById_ReturnsTheRental()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var customer = await Client.CreateCustomerAsync();
        var started = await Client.StartRentalAsync(vehicle.Id, customer.Id, 5);

        var response = await Client.GetAsync($"/api/v1/rentals/{started.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(started, await response.ReadAsync<RentalDto>());
    }

    [DatabaseFact]
    public async Task GetRentalById_Unknown_Returns404ProblemDetails()
    {
        var response = await Client.GetAsync($"/api/v1/rentals/{Guid.NewGuid()}");

        await response.AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task GetRentals_WithNoRentals_ReturnsAnEmptyPage()
    {
        var page = await (await Client.GetAsync("/api/v1/rentals")).ReadAsync<PagedResult<RentalDto>>();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [DatabaseFact]
    public async Task GetRentals_ReturnsActiveAndCompletedRentalsInTheHistory()
    {
        var v1 = await Client.CreateVehicleAsync("ABC-123");
        var v2 = await Client.CreateVehicleAsync("DEF-456");
        var customer = await Client.CreateCustomerAsync();
        var first = await Client.StartRentalAsync(v1.Id, customer.Id);
        await Client.StartRentalAsync(v2.Id, customer.Id);
        await Client.PostAsync($"/api/v1/rentals/{first.Id}/return", null);

        var page = await (await Client.GetAsync("/api/v1/rentals")).ReadAsync<PagedResult<RentalDto>>();

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, r => r.Status == RentalStatus.Completed);
        Assert.Contains(page.Items, r => r.Status == RentalStatus.Active);
    }

    [DatabaseFact]
    public async Task GetRentals_Paging_ReturnsTheRequestedSlice()
    {
        var customer = await Client.CreateCustomerAsync();
        for (int i = 1; i <= 5; i++)
        {
            var vehicle = await Client.CreateVehicleAsync($"VEH-{i:00}");
            await Client.StartRentalAsync(vehicle.Id, customer.Id);
        }

        var page2 = await (await Client.GetAsync("/api/v1/rentals?page=2&pageSize=2")).ReadAsync<PagedResult<RentalDto>>();
        var page3 = await (await Client.GetAsync("/api/v1/rentals?page=3&pageSize=2")).ReadAsync<PagedResult<RentalDto>>();
        var page9 = await (await Client.GetAsync("/api/v1/rentals?page=9&pageSize=2")).ReadAsync<PagedResult<RentalDto>>();

        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(3, page2.TotalPages);
        Assert.Single(page3.Items);
        Assert.Empty(page9.Items);
        Assert.Equal(5, page9.TotalCount);
    }

    [DatabaseFact]
    public async Task GetRentals_PagesDoNotOverlapAndTogetherCoverEveryRental()
    {
        var customer = await Client.CreateCustomerAsync();
        for (int i = 1; i <= 5; i++)
        {
            var vehicle = await Client.CreateVehicleAsync($"VEH-{i:00}");
            await Client.StartRentalAsync(vehicle.Id, customer.Id);
        }

        var ids = new List<Guid>();
        for (int page = 1; page <= 3; page++)
        {
            var result = await (await Client.GetAsync($"/api/v1/rentals?page={page}&pageSize=2")).ReadAsync<PagedResult<RentalDto>>();
            ids.AddRange(result.Items.Select(r => r.Id));
        }

        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }

    // ----- Returning -----

    [DatabaseFact]
    public async Task ReturnRental_CompletesIt_RecordsTheReturnDate_AndFreesTheVehicle()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123", 100m);
        var customer = await Client.CreateCustomerAsync();
        var started = await Client.StartRentalAsync(vehicle.Id, customer.Id, 3);

        var response = await Client.PostAsync($"/api/v1/rentals/{started.Id}/return", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = await response.ReadAsync<RentalDto>();
        Assert.Equal(RentalStatus.Completed, completed.Status);
        Assert.Equal(Today, completed.ActualReturnDate);
        Assert.Equal(started.TotalCost, completed.TotalCost);
        var reloaded = await (await Client.GetAsync($"/api/v1/vehicles/{vehicle.Id}")).ReadAsync<VehicleDto>();
        Assert.Equal(VehicleAvailabilityStatus.Available, reloaded.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task ReturnRental_Twice_Returns409ProblemDetailsTheSecondTime()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var customer = await Client.CreateCustomerAsync();
        var started = await Client.StartRentalAsync(vehicle.Id, customer.Id);
        await Client.PostAsync($"/api/v1/rentals/{started.Id}/return", null);

        var second = await Client.PostAsync($"/api/v1/rentals/{started.Id}/return", null);

        var problem = await second.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Contains("already been completed", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task ReturnRental_Unknown_Returns404ProblemDetails()
    {
        var response = await Client.PostAsync($"/api/v1/rentals/{Guid.NewGuid()}/return", null);

        await response.AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task AVehicleCanBeRentedAgain_AfterItIsReturned()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var customer = await Client.CreateCustomerAsync();
        var first = await Client.StartRentalAsync(vehicle.Id, customer.Id);
        await Client.PostAsync($"/api/v1/rentals/{first.Id}/return", null);

        var second = await Client.StartRentalRawAsync(vehicle.Id, customer.Id);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [DatabaseFact]
    public async Task HistoricalTotal_StaysTheSame_WhenTheVehicleIsRentedAgainUnderDifferentPricing()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123", 100m);
        var customer = await Client.CreateCustomerAsync();
        var first = await Client.StartRentalAsync(vehicle.Id, customer.Id, days: 5);
        await Client.PostAsync($"/api/v1/rentals/{first.Id}/return", null);

        var second = await Client.StartRentalAsync(vehicle.Id, customer.Id, days: 10);

        var reloadedFirst = await (await Client.GetAsync($"/api/v1/rentals/{first.Id}")).ReadAsync<RentalDto>();
        var reloadedSecond = await (await Client.GetAsync($"/api/v1/rentals/{second.Id}")).ReadAsync<RentalDto>();
        Assert.Equal(500m, reloadedFirst.TotalCost);
        Assert.StartsWith("Normal", reloadedFirst.PricingDescription);
        Assert.Equal(800m, reloadedSecond.TotalCost);
        Assert.StartsWith("Long-term", reloadedSecond.PricingDescription);
    }
}
