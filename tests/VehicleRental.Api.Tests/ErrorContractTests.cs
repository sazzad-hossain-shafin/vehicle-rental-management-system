using System.Net;
using System.Text;
using VehicleRental.Api.Tests.Support;

namespace VehicleRental.Api.Tests;

/// <summary>
/// The shape of error responses and request validation. None of these need a database: they are decided
/// before any data is read, or they deliberately point the API at a database that does not exist.
/// </summary>
public class ErrorContractTests : IDisposable
{
    private readonly ApiFactory _factory = new(ApiFactory.UnreachableDatabase);
    private readonly HttpClient _client;

    // Signed in as staff, so requests get past authentication and reach the validation and error handling
    // these tests are about. (Authentication itself is tested separately.)
    public ErrorContractTests() => _client = _factory.CreateClientAs(VehicleRental.Application.Security.Roles.Staff);

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // ----- 400 -----

    [Fact]
    public async Task MalformedJson_Returns400ProblemDetails()
    {
        var response = await _client.PostAsync("/api/v1/vehicles", Json("{\"make\":"));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingRequiredFields_Return400ValidationProblemNamingEachField()
    {
        var response = await _client.PostAsync("/api/v1/vehicles", Json("{\"make\":\"Toyota\"}"));

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors");
        foreach (string field in new[] { "registrationNumber", "model", "year", "vehicleType", "dailyRate" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Expected a validation error for '{field}'.");
        }

        Assert.False(errors.TryGetProperty("make", out _));
    }

    [Fact]
    public async Task EmptyBody_Returns400ProblemDetails()
    {
        var response = await _client.PostAsync("/api/v1/customers", new StringContent("", Encoding.UTF8, "application/json"));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhitespaceOnlyRequiredText_Returns400ValidationProblem()
    {
        var response = await _client.PostAsync(
            "/api/v1/customers", Json("{\"customerNumber\":\"   \",\"name\":\"Alice\"}"));

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("customerNumber", out _));
    }

    [Theory]
    [InlineData("\"vehicleType\":\"Plane\"")]
    [InlineData("\"vehicleType\":1")]
    [InlineData("\"year\":\"abc\"")]
    [InlineData("\"dailyRate\":\"cheap\"")]
    public async Task InvalidEnumOrNumber_InBody_Returns400ProblemDetails(string badField)
    {
        string body = "{\"registrationNumber\":\"X-1\",\"make\":\"A\",\"model\":\"B\",\"year\":2020,\"vehicleType\":\"Car\",\"dailyRate\":10,"
                      + badField + "}";

        var response = await _client.PostAsync("/api/v1/vehicles", Json(body));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/vehicles/not-a-guid")]
    [InlineData("/api/v1/customers/12345")]
    [InlineData("/api/v1/rentals/not-a-guid")]
    public async Task InvalidGuidInRoute_Returns400ProblemDetails(string url)
    {
        var response = await _client.GetAsync(url);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidGuidInRentalBody_Returns400ProblemDetails()
    {
        var response = await _client.PostAsync(
            "/api/v1/rentals", Json("{\"vehicleId\":\"nope\",\"customerId\":\"nope\",\"rentalDays\":3}"));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingRentalFields_Return400ValidationProblem()
    {
        var response = await _client.PostAsync("/api/v1/rentals", Json("{}"));

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("vehicleId", out _));
        Assert.True(errors.TryGetProperty("customerId", out _));
        Assert.True(errors.TryGetProperty("rentalDays", out _));
    }

    [Theory]
    [InlineData("/api/v1/vehicles?vehicleType=Plane", "vehicleType")]
    [InlineData("/api/v1/vehicles?vehicleType=1", "vehicleType")]
    [InlineData("/api/v1/vehicles?availability=Broken", "availability")]
    public async Task InvalidEnumFilter_Returns400NamingTheAllowedValues(string url, string field)
    {
        var response = await _client.GetAsync(url);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        string message = problem.GetProperty("errors").GetProperty(field)[0].GetString()!;
        Assert.Contains("Must be one of", message);
    }

    [Theory]
    [InlineData("/api/v1/vehicles?page=abc")]
    [InlineData("/api/v1/vehicles?maxDailyRate=cheap")]
    [InlineData("/api/v1/rentals?pageSize=many")]
    public async Task NonNumericQueryValues_Return400ProblemDetails(string url)
    {
        var response = await _client.GetAsync(url);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/vehicles?page=0")]
    [InlineData("/api/v1/vehicles?pageSize=0")]
    [InlineData("/api/v1/vehicles?pageSize=101")]
    [InlineData("/api/v1/vehicles?maxDailyRate=-1")]
    [InlineData("/api/v1/rentals?page=-3")]
    [InlineData("/api/v1/rentals?pageSize=1000")]
    public async Task OutOfRangePagingOrFilters_Return400WithAClientFriendlyMessage(string url)
    {
        var response = await _client.GetAsync(url);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        string detail = problem.GetProperty("detail").GetString()!;
        Assert.DoesNotContain("(Parameter", detail);
        Assert.DoesNotContain("Actual value", detail);
    }

    [Fact]
    public async Task BodyTooLargeForTheServer_IsNotAnError_ButAnOrdinaryRequestIsStillAccepted()
    {
        // The 64 KB limit is enforced by Kestrel, which the in-memory test server does not run; this only
        // documents that normal-sized bodies are not affected by it.
        var response = await _client.PostAsync("/api/v1/customers", Json("{\"customerNumber\":\"C1\"}"));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    // ----- Security baseline -----

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/nothing-here")]
    [InlineData("/api/v1/vehicles/not-a-guid")]
    public async Task Responses_CarryTheNoSniffHeader(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
    }

    // ----- 404 / 405 -----

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/nothing-here");

        await response.AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WrongHttpMethod_Returns405ProblemDetails()
    {
        var response = await _client.DeleteAsync("/api/v1/vehicles");

        await response.AssertProblemAsync(HttpStatusCode.MethodNotAllowed);
    }

    // ----- 500 -----

    [Fact]
    public async Task UnexpectedFailure_InProduction_Returns500WithoutAnyInternalDetail()
    {
        await using var production = new ApiFactory(ApiFactory.UnreachableDatabase, "Production");
        using var client = production.CreateClient();

        // Reading data fails because the database cannot be reached: a genuine unexpected error.
        var response = await client.GetAsync("/api/v1/vehicles/" + Guid.NewGuid());

        var problem = await response.AssertProblemAsync(HttpStatusCode.InternalServerError);
        AssertNoInternalDetail(await response.Content.ReadAsStringAsync());
        Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind == System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public async Task UnexpectedFailure_InDevelopment_StillExposesNoStackTraceOrConnectionDetails()
    {
        var response = await _client.GetAsync("/api/v1/vehicles/" + Guid.NewGuid());

        await response.AssertProblemAsync(HttpStatusCode.InternalServerError);
        AssertNoInternalDetail(await response.Content.ReadAsStringAsync());
    }

    private static void AssertNoInternalDetail(string body)
    {
        Assert.DoesNotContain("   at ", body);              // stack frames
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("127.0.0.1", body);           // connection details
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain(".cs", body);                 // source paths
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
    }
}
