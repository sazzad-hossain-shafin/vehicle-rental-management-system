using System.Net;
using System.Text.Json;
using VehicleRental.Api.Tests.Support;

namespace VehicleRental.Api.Tests;

/// <summary>
/// The generated API description, the documentation page, and the health endpoints that need no database.
/// </summary>
public class OpenApiAndHealthTests
{
    private static async Task<JsonElement> GetOpenApiDocumentAsync()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.ReadJsonAsync();
    }

    [Fact]
    public async Task OpenApiDocument_ListsEveryIntendedRouteWithTheRightMethod()
    {
        var document = await GetOpenApiDocumentAsync();
        var paths = document.GetProperty("paths");

        var expected = new (string Path, string Method)[]
        {
            ("/api/v1/vehicles", "get"),
            ("/api/v1/vehicles", "post"),
            ("/api/v1/vehicles/{id}", "get"),
            ("/api/v1/vehicles/by-registration/{registrationNumber}", "get"),
            ("/api/v1/customers", "post"),
            ("/api/v1/customers/{id}", "get"),
            ("/api/v1/customers/by-number/{customerNumber}", "get"),
            ("/api/v1/rentals", "post"),
            ("/api/v1/rentals", "get"),
            ("/api/v1/rentals/{id}", "get"),
            ("/api/v1/rentals/{id}/return", "post"),
            ("/api/v1/auth/login", "post"),
            ("/api/v1/auth/register", "post"),
            ("/api/v1/admin/staff", "post"),
            ("/api/v1/me", "get"),
            ("/api/v1/me/customer", "get"),
            ("/api/v1/me/rentals", "get"),
            ("/api/v1/vehicles/availability", "get"),
            ("/api/v1/reservations", "post"),
            ("/api/v1/reservations", "get"),
            ("/api/v1/reservations/{id}", "get"),
            ("/api/v1/reservations/{id}/cancel", "post"),
            ("/api/v1/reservations/{id}/pickup", "post"),
            ("/api/v1/me/reservations", "post"),
            ("/api/v1/me/reservations", "get"),
            ("/api/v1/me/reservations/{id}", "get"),
            ("/api/v1/me/reservations/{id}/cancel", "post")
        };

        foreach (var (path, method) in expected)
        {
            Assert.True(paths.TryGetProperty(path, out var item), $"Missing route {path}");
            Assert.True(item.TryGetProperty(method, out _), $"Missing {method.ToUpper()} {path}");
        }

        int operations = paths.EnumerateObject().Sum(p => p.Value.EnumerateObject().Count());
        Assert.Equal(expected.Length, operations); // nothing unintended is exposed
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheStatusCodesOfCreateAndReturn()
    {
        var document = await GetOpenApiDocumentAsync();
        var paths = document.GetProperty("paths");

        var createVehicle = paths.GetProperty("/api/v1/vehicles").GetProperty("post").GetProperty("responses");
        Assert.True(createVehicle.TryGetProperty("201", out _));
        Assert.True(createVehicle.TryGetProperty("400", out _));
        Assert.True(createVehicle.TryGetProperty("409", out _));

        var returnRental = paths.GetProperty("/api/v1/rentals/{id}/return").GetProperty("post").GetProperty("responses");
        Assert.True(returnRental.TryGetProperty("200", out _));
        Assert.True(returnRental.TryGetProperty("404", out _));
        Assert.True(returnRental.TryGetProperty("409", out _));
    }

    [Fact]
    public async Task OpenApiDocument_UsesNamedOperationsAndTags()
    {
        var document = await GetOpenApiDocumentAsync();
        var start = document.GetProperty("paths").GetProperty("/api/v1/rentals").GetProperty("post");

        Assert.Equal("StartRental", start.GetProperty("operationId").GetString());
        Assert.Equal("Rentals", start.GetProperty("tags")[0].GetString());
        Assert.Equal("Vehicle Rental API", document.GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_ExposesContractTypesOnly_AsReadableEnums()
    {
        var document = await GetOpenApiDocumentAsync();
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var names = schemas.EnumerateObject().Select(s => s.Name).ToList();

        Assert.Contains("CreateVehicleRequest", names);
        Assert.Contains("StartRentalApiRequest", names);
        Assert.Contains("RentalDto", names);

        // No domain entity, EF or persistence type appears.
        Assert.DoesNotContain("Vehicle", names);
        Assert.DoesNotContain("Rental", names);
        Assert.DoesNotContain("Customer", names);
        Assert.DoesNotContain(names, n => n.Contains("DbContext") || n.Contains("Entity") || n.Contains("xmin"));

        var vehicleTypes = schemas.GetProperty("VehicleType").GetProperty("enum").EnumerateArray().Select(e => e.GetString());
        Assert.Equal(new[] { "Car", "Motorcycle", "Van" }, vehicleTypes);
    }

    [Fact]
    public async Task OpenApiDocument_DoesNotContainSecretsOrServerDetails()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, "Development");
        using var client = factory.CreateClient();

        string text = await client.GetStringAsync("/openapi/v1.json");

        Assert.DoesNotContain(factory.SigningKey, text);   // the real signing key
        Assert.DoesNotContain("Host=", text);              // connection details
        Assert.DoesNotContain("ConnectionString", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", text, StringComparison.OrdinalIgnoreCase); // no Identity internals
        Assert.DoesNotContain("SecurityStamp", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocumentationEndpoints_AreAvailableInDevelopment()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, "Development");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);

        var page = await client.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("text/html", page.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task DocumentationEndpoints_AreNotExposedInProduction()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, "Production");
        using var signedIn = factory.CreateClientAs(VehicleRental.Application.Security.Roles.Admin);
        using var anonymous = factory.CreateClient();

        foreach (string path in new[] { "/openapi/v1.json", "/scalar/v1" })
        {
            // The routes are not mapped at all, as a signed-in caller can tell...
            Assert.Equal(HttpStatusCode.NotFound, (await signedIn.GetAsync(path)).StatusCode);

            // ...and an anonymous caller never gets the documentation either.
            Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync(path)).StatusCode);
        }
    }

    // ----- Health -----

    [Fact]
    public async Task LivenessCheck_IsHealthy_EvenWhenTheDatabaseIsDown()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReadinessCheck_WhenTheDatabaseIsUnreachable_Returns503_WithoutConnectionDetails()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        var check = body.GetProperty("checks")[0];
        Assert.Equal("database", check.GetProperty("name").GetString());
        Assert.Equal("The database is unreachable.", check.GetProperty("description").GetString());

        string text = body.GetRawText();
        Assert.DoesNotContain("127.0.0.1", text);
        Assert.DoesNotContain("Host=", text);
        Assert.DoesNotContain("Npgsql", text);
    }

    [Fact]
    public async Task ReadinessCheck_WhenNoConnectionStringIsConfigured_IsUnhealthy_NotACrash()
    {
        await using var factory = new ApiFactory("");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        // The check itself fails (and is reported as unhealthy) instead of taking the service down.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Host=", await response.Content.ReadAsStringAsync());
    }
}
