using System.Text.Json;
using VehicleRental.Api.Tests.Support;

namespace VehicleRental.Api.Tests;

/// <summary>The security information in the generated API description. No database needed.</summary>
public class OpenApiSecurityTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private JsonElement _document;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(ApiFactory.UnreachableDatabase, "Development");
        using var client = _factory.CreateClient();
        _document = await (await client.GetAsync("/openapi/v1.json")).ReadJsonAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private JsonElement Operation(string path, string method) =>
        _document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static bool RequiresBearer(JsonElement operation) =>
        operation.TryGetProperty("security", out var security)
        && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("Bearer", out _));

    [Fact]
    public void TheDocument_DescribesABearerJwtScheme()
    {
        var scheme = _document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    [Theory]
    [InlineData("/api/v1/vehicles", "post")]
    [InlineData("/api/v1/customers", "post")]
    [InlineData("/api/v1/customers/{id}", "get")]
    [InlineData("/api/v1/rentals", "post")]
    [InlineData("/api/v1/rentals", "get")]
    [InlineData("/api/v1/rentals/{id}", "get")]
    [InlineData("/api/v1/rentals/{id}/return", "post")]
    [InlineData("/api/v1/admin/staff", "post")]
    [InlineData("/api/v1/me", "get")]
    [InlineData("/api/v1/me/customer", "get")]
    [InlineData("/api/v1/me/rentals", "get")]
    public void ProtectedOperations_RequireTheBearerScheme_AndDocumentA401(string path, string method)
    {
        var operation = Operation(path, method);

        Assert.True(RequiresBearer(operation), $"{method.ToUpper()} {path} should require a bearer token.");
        Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
    }

    [Theory]
    [InlineData("/api/v1/vehicles", "get")]
    [InlineData("/api/v1/vehicles/{id}", "get")]
    [InlineData("/api/v1/vehicles/by-registration/{registrationNumber}", "get")]
    [InlineData("/api/v1/auth/login", "post")]
    [InlineData("/api/v1/auth/register", "post")]
    public void PublicOperations_DoNotRequireAToken(string path, string method)
    {
        Assert.False(RequiresBearer(Operation(path, method)), $"{method.ToUpper()} {path} is public.");
    }

    [Theory]
    [InlineData("/api/v1/vehicles", "post")]
    [InlineData("/api/v1/rentals", "get")]
    [InlineData("/api/v1/admin/staff", "post")]
    [InlineData("/api/v1/me/rentals", "get")]
    public void RoleRestrictedOperations_AlsoDocumentA403(string path, string method)
    {
        Assert.True(Operation(path, method).GetProperty("responses").TryGetProperty("403", out _));
    }

    [Fact]
    public void AnyAuthenticatedOperation_DoesNotClaimA403_ItCannotReturn()
    {
        // /me only needs a sign-in, not a particular role.
        Assert.False(Operation("/api/v1/me", "get").GetProperty("responses").TryGetProperty("403", out _));
    }

    [Fact]
    public void TheLoginRequest_HasNoRoleField_AndTheRegisterRequest_CannotChooseOne()
    {
        var schemas = _document.GetProperty("components").GetProperty("schemas");

        foreach (string name in new[] { "LoginRequest", "RegisterCustomerRequest", "CreateStaffRequest" })
        {
            var properties = schemas.GetProperty(name).GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

            Assert.DoesNotContain(properties, p => p.Contains("role", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(properties, p => p.Equals("customerId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void NoIdentityOrSecurityInternals_AppearInTheDocument()
    {
        string text = _document.GetRawText();

        foreach (string forbidden in new[] { "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "LockoutEnd", "AccessFailedCount", "NormalizedEmail" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
