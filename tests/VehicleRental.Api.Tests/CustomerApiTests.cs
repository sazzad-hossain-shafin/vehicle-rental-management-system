using System.Net;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application.Customers;

namespace VehicleRental.Api.Tests;

public class CustomerApiTests : ApiTestBase
{
    public CustomerApiTests(PostgresApiFixture api) : base(api)
    {
    }

    [DatabaseFact]
    public async Task PostCustomer_WithValidData_Returns201WithLocationAndTheNewCustomer()
    {
        var response = await Client.PostJsonAsync("/api/v1/customers", new { customerNumber = "c-100", name = "  Alice " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = await response.ReadAsync<CustomerDto>();
        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Equal("C-100", customer.CustomerNumber);
        Assert.Equal("Alice", customer.Name);
        Assert.EndsWith($"/api/v1/customers/{customer.Id}", response.Headers.Location!.ToString());
    }

    [DatabaseFact]
    public async Task PostCustomer_IgnoresAnIdSentByTheClient()
    {
        var sentId = Guid.NewGuid();

        var response = await Client.PostJsonAsync("/api/v1/customers", new { id = sentId, customerNumber = "C1", name = "Alice" });

        Assert.NotEqual(sentId, (await response.ReadAsync<CustomerDto>()).Id);
    }

    [DatabaseFact]
    public async Task GetCustomerById_ReturnsTheCustomer()
    {
        var created = await Client.CreateCustomerAsync("C1", "Alice");

        var response = await Client.GetAsync($"/api/v1/customers/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Alice", (await response.ReadAsync<CustomerDto>()).Name);
    }

    [DatabaseFact]
    public async Task GetCustomerByNumber_IsCaseInsensitive()
    {
        var created = await Client.CreateCustomerAsync("C-100", "Alice");

        var response = await Client.GetAsync("/api/v1/customers/by-number/c-100");

        Assert.Equal(created.Id, (await response.ReadAsync<CustomerDto>()).Id);
    }

    [DatabaseFact]
    public async Task GetCustomer_Unknown_Returns404ProblemDetails()
    {
        await (await Client.GetAsync($"/api/v1/customers/{Guid.NewGuid()}")).AssertProblemAsync(HttpStatusCode.NotFound);
        await (await Client.GetAsync("/api/v1/customers/by-number/NOPE")).AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task PostCustomer_WithAnExistingNumber_Returns409_EvenWithTheSameName()
    {
        await Client.CreateCustomerAsync("C1", "Alice");

        var sameName = await Client.PostJsonAsync("/api/v1/customers", new { customerNumber = "c1", name = "Alice" });
        var otherName = await Client.PostJsonAsync("/api/v1/customers", new { customerNumber = "C1", name = "Mallory" });

        await sameName.AssertProblemAsync(HttpStatusCode.Conflict);
        await otherName.AssertProblemAsync(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task PostCustomer_ThatConflicts_DoesNotChangeTheStoredCustomer()
    {
        var original = await Client.CreateCustomerAsync("C1", "Alice");
        await Client.PostJsonAsync("/api/v1/customers", new { customerNumber = "C1", name = "Mallory" });

        var stored = await (await Client.GetAsync($"/api/v1/customers/{original.Id}")).ReadAsync<CustomerDto>();

        Assert.Equal("Alice", stored.Name);
    }

    [DatabaseFact]
    public async Task PostCustomer_BreakingADomainRule_Returns400ProblemDetails()
    {
        var response = await Client.PostJsonAsync("/api/v1/customers", new
        {
            customerNumber = new string('9', 50),
            name = "Alice"
        });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }
}
