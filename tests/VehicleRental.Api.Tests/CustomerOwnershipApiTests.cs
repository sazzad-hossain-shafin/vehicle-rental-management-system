using System.Net;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Security;
using VehicleRental.Application.Vehicles;

namespace VehicleRental.Api.Tests;

/// <summary>
/// A customer must only ever reach their own data, whatever IDs they put in the URL, query string or headers
/// (the classic "insecure direct object reference" attack). Two customers each have a rental; every test
/// tries to cross from one to the other.
/// </summary>
public class CustomerOwnershipApiTests : ApiTestBase
{
    public CustomerOwnershipApiTests(PostgresApiFixture api) : base(api)
    {
    }

    private sealed record Scenario(
        CustomerSession Alice,
        CustomerSession Bob,
        RentalDto AliceRental,
        RentalDto BobRental,
        VehicleDto AliceVehicle,
        VehicleDto BobVehicle);

    private async Task<Scenario> SetUpAsync()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice Example");
        var bob = await Api.CreateCustomerClientAsync("Bob Example");
        var aliceVehicle = await Client.CreateVehicleAsync("ALI-001");
        var bobVehicle = await Client.CreateVehicleAsync("BOB-001");

        // The desk (staff) starts the rentals, as customers cannot.
        var aliceRental = await Client.StartRentalAsync(aliceVehicle.Id, alice.User.CustomerId!.Value, 3);
        var bobRental = await Client.StartRentalAsync(bobVehicle.Id, bob.User.CustomerId!.Value, 5);

        return new Scenario(alice, bob, aliceRental, bobRental, aliceVehicle, bobVehicle);
    }

    private static string MaskIds(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "<id>");

    // ----- Own data -----

    [DatabaseFact]
    public async Task ACustomer_CanReadTheirOwnProfile()
    {
        var s = await SetUpAsync();

        var response = await s.Alice.Client.GetAsync("/api/v1/me/customer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.ReadAsync<CustomerDto>();
        Assert.Equal(s.Alice.User.CustomerId, profile.Id);
        Assert.Equal("Alice Example", profile.Name);
    }

    [DatabaseFact]
    public async Task ACustomer_SeesOnlyTheirOwnRentals()
    {
        var s = await SetUpAsync();

        var page = await (await s.Alice.Client.GetAsync("/api/v1/me/rentals")).ReadAsync<PagedResult<RentalDto>>();

        Assert.Equal(new[] { s.AliceRental.Id }, page.Items.Select(r => r.Id));
        Assert.Equal(1, page.TotalCount); // the count never includes anyone else's rentals either
        Assert.All(page.Items, r => Assert.Equal(s.Alice.User.CustomerId, r.CustomerId));
    }

    [DatabaseFact]
    public async Task ACustomer_CanReadOneOfTheirOwnRentalsById()
    {
        var s = await SetUpAsync();

        var response = await s.Alice.Client.GetAsync($"/api/v1/rentals/{s.AliceRental.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(s.AliceRental, await response.ReadAsync<RentalDto>());
    }

    [DatabaseFact]
    public async Task ACustomerWithNoRentals_GetsAnEmptyPage()
    {
        var newcomer = await Api.CreateCustomerClientAsync();

        var page = await (await newcomer.Client.GetAsync("/api/v1/me/rentals")).ReadAsync<PagedResult<RentalDto>>();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [DatabaseFact]
    public async Task MyRentals_IsPaged_AndOnlyCountsMyRentals()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        for (int i = 1; i <= 3; i++)
        {
            var vehicle = await Client.CreateVehicleAsync($"ALI-{i:000}");
            await Client.StartRentalAsync(vehicle.Id, alice.User.CustomerId!.Value);
        }
        var bobVehicle = await Client.CreateVehicleAsync("BOB-001");
        await Client.StartRentalAsync(bobVehicle.Id, bob.User.CustomerId!.Value);

        var page = await (await alice.Client.GetAsync("/api/v1/me/rentals?page=2&pageSize=2"))
            .ReadAsync<PagedResult<RentalDto>>();

        Assert.Single(page.Items);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }

    [DatabaseFact]
    public async Task MyRentals_RejectsOutOfRangePaging()
    {
        var alice = await Api.CreateCustomerClientAsync();

        var response = await alice.Client.GetAsync("/api/v1/me/rentals?pageSize=1000");

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    // ----- Another customer's data: the IDOR attempts -----

    [DatabaseFact]
    public async Task ACustomer_CannotReadAnotherCustomersRentalById_ItIsIndistinguishableFromMissing()
    {
        var s = await SetUpAsync();

        var someoneElses = await s.Alice.Client.GetAsync($"/api/v1/rentals/{s.BobRental.Id}");
        var missing = await s.Alice.Client.GetAsync($"/api/v1/rentals/{Guid.NewGuid()}");

        var otherProblem = await someoneElses.AssertProblemAsync(HttpStatusCode.NotFound);
        var missingProblem = await missing.AssertProblemAsync(HttpStatusCode.NotFound);

        // Same status, same wording: Alice cannot tell "exists but not yours" from "does not exist".
        Assert.Equal(missingProblem.GetProperty("title").GetString(), otherProblem.GetProperty("title").GetString());
        Assert.Equal(MaskIds(missingProblem.GetProperty("detail").GetString()!), MaskIds(otherProblem.GetProperty("detail").GetString()!));

        // And nothing of Bob's rental leaks into the response.
        string text = await someoneElses.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Bob", text);
        Assert.DoesNotContain("BOB-001", text);
    }

    [DatabaseFact]
    public async Task ACustomer_CannotReadAnotherCustomersProfile_ByIdOrNumber()
    {
        var s = await SetUpAsync();

        var byId = await s.Alice.Client.GetAsync($"/api/v1/customers/{s.Bob.User.CustomerId}");
        var byNumber = await s.Alice.Client.GetAsync($"/api/v1/customers/by-number/{s.Bob.User.CustomerNumber}");

        Assert.Equal(HttpStatusCode.Forbidden, byId.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byNumber.StatusCode);
        Assert.DoesNotContain("Bob", await byId.Content.ReadAsStringAsync());
    }

    [DatabaseFact]
    public async Task ACustomer_CannotUseTheStaffCustomerEndpoint_EvenForTheirOwnId()
    {
        var s = await SetUpAsync();

        var response = await s.Alice.Client.GetAsync($"/api/v1/customers/{s.Alice.User.CustomerId}");

        // Customers use /me/customer; the desk endpoint is for staff.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DatabaseFact]
    public async Task ACustomer_CannotListEveryonesRentals()
    {
        var s = await SetUpAsync();

        var response = await s.Alice.Client.GetAsync("/api/v1/rentals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Bob", await response.Content.ReadAsStringAsync());
    }

    [DatabaseTheory]
    [InlineData("customerId")]
    [InlineData("customer")]
    [InlineData("customerNumber")]
    [InlineData("userId")]
    [InlineData("id")]
    public async Task ChangingAQueryStringValue_DoesNotChangeWhoseRentalsAreReturned(string parameter)
    {
        var s = await SetUpAsync();

        var page = await (await s.Alice.Client.GetAsync($"/api/v1/me/rentals?{parameter}={s.Bob.User.CustomerId}"))
            .ReadAsync<PagedResult<RentalDto>>();

        Assert.Equal(new[] { s.AliceRental.Id }, page.Items.Select(r => r.Id));
    }

    [DatabaseFact]
    public async Task AnIdSentInAHeader_DoesNotChangeWhoseDataIsReturned()
    {
        var s = await SetUpAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/customer");
        request.Headers.Add("X-Customer-Id", s.Bob.User.CustomerId.ToString());
        request.Headers.Add("X-Forwarded-User", s.Bob.User.Id.ToString());

        var profile = await (await s.Alice.Client.SendAsync(request)).ReadAsync<CustomerDto>();

        Assert.Equal(s.Alice.User.CustomerId, profile.Id);
    }

    [DatabaseFact]
    public async Task ABodyNamingAnotherCustomer_CannotMakeACustomerActOnTheirBehalf()
    {
        var s = await SetUpAsync();

        // Customers cannot start rentals at all, for themselves or anyone else.
        var spare = await Client.CreateVehicleAsync("SPARE-1");
        var response = await s.Alice.Client.StartRentalRawAsync(spare.Id, s.Bob.User.CustomerId!.Value);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var stillAvailable = await (await Client.GetAsync($"/api/v1/vehicles/{spare.Id}")).ReadAsync<VehicleDto>();
        Assert.Equal(VehicleRental.Domain.Enums.VehicleAvailabilityStatus.Available, stillAvailable.AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task ACustomer_CannotReturnTheirOwnOrAnyoneElsesRental()
    {
        var s = await SetUpAsync();

        var own = await s.Alice.Client.PostAsync($"/api/v1/rentals/{s.AliceRental.Id}/return", null);
        var other = await s.Alice.Client.PostAsync($"/api/v1/rentals/{s.BobRental.Id}/return", null);

        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        var unchanged = await (await Client.GetAsync($"/api/v1/rentals/{s.BobRental.Id}")).ReadAsync<RentalDto>();
        Assert.Equal(VehicleRental.Domain.Enums.RentalStatus.Active, unchanged.Status);
    }

    [DatabaseFact]
    public async Task ACustomer_CannotCreateVehiclesOrCustomers()
    {
        var alice = await Api.CreateCustomerClientAsync();

        var vehicle = await alice.Client.PostJsonAsync("/api/v1/vehicles", new
        {
            registrationNumber = "EVIL-1", make = "A", model = "B", year = 2020, vehicleType = "Car", dailyRate = 1
        });
        var customer = await alice.Client.PostJsonAsync("/api/v1/customers", new { customerNumber = "EVIL", name = "Evil" });

        Assert.Equal(HttpStatusCode.Forbidden, vehicle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
    }

    [DatabaseFact]
    public async Task ATokenCannotBeUsedToClaimAnotherCustomersIdentity()
    {
        var s = await SetUpAsync();

        // Anyone can build a token claiming to be Bob, but without the server's key the signature is wrong.
        string forged = TestTokens.Create(
            ApiFactory.NewRandomKey(), [Roles.Customer], s.Bob.User.Id, s.Bob.User.CustomerId);
        using var attacker = Api.Factory.CreateClientWithToken(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.GetAsync("/api/v1/me/customer")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.GetAsync("/api/v1/me/rentals")).StatusCode);
    }

    // ----- Staff and admins see everything -----

    [DatabaseFact]
    public async Task Staff_CanSeeEveryRental_AndAnyRentalById()
    {
        var s = await SetUpAsync();

        var all = await (await Client.GetAsync("/api/v1/rentals")).ReadAsync<PagedResult<RentalDto>>();
        var bobsById = await Client.GetAsync($"/api/v1/rentals/{s.BobRental.Id}");

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(HttpStatusCode.OK, bobsById.StatusCode);
    }

    [DatabaseFact]
    public async Task Staff_CanReadAnyCustomer_AndAdminsToo()
    {
        var s = await SetUpAsync();
        using var admin = await Api.CreateAdminClientAsync();

        var asStaff = await Client.GetAsync($"/api/v1/customers/{s.Bob.User.CustomerId}");
        var asAdmin = await admin.GetAsync($"/api/v1/customers/{s.Bob.User.CustomerId}");

        Assert.Equal(HttpStatusCode.OK, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }

    [DatabaseFact]
    public async Task Staff_HaveNoCustomerProfileOrCustomerRentals_OfTheirOwn()
    {
        var response = await Client.GetAsync("/api/v1/me/customer");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DatabaseFact]
    public async Task OwnershipHolds_BothWays_AndAfterARentalIsReturned()
    {
        var s = await SetUpAsync();
        await Client.PostAsync($"/api/v1/rentals/{s.AliceRental.Id}/return", null);

        var aliceSeesBob = await s.Alice.Client.GetAsync($"/api/v1/rentals/{s.BobRental.Id}");
        var bobSeesAlice = await s.Bob.Client.GetAsync($"/api/v1/rentals/{s.AliceRental.Id}");
        var aliceSeesOwnCompleted = await s.Alice.Client.GetAsync($"/api/v1/rentals/{s.AliceRental.Id}");

        Assert.Equal(HttpStatusCode.NotFound, aliceSeesBob.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobSeesAlice.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aliceSeesOwnCompleted.StatusCode);
        Assert.Equal(VehicleRental.Domain.Enums.RentalStatus.Completed, (await aliceSeesOwnCompleted.ReadAsync<RentalDto>()).Status);
    }

    // ----- Public vehicle browsing still works for a customer, and for no one -----

    [DatabaseFact]
    public async Task VehicleBrowsing_IsOpenToEveryone()
    {
        await Client.CreateVehicleAsync("PUB-001");
        var customer = await Api.CreateCustomerClientAsync();
        using var anonymous = Api.CreateAnonymousClient();

        foreach (var client in new[] { anonymous, customer.Client, Client })
        {
            var page = await (await client.GetAsync("/api/v1/vehicles")).ReadAsync<PagedResult<VehicleDto>>();
            Assert.Equal(1, page.TotalCount);
        }
    }
}
