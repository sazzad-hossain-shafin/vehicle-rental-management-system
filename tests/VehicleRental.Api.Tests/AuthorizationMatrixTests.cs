using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application.Security;

namespace VehicleRental.Api.Tests;

/// <summary>
/// Who may call what. Roles are checked with tokens signed by the test's own key, so no database is needed:
/// a request that is allowed through authorization goes on to fail later (the database is unreachable),
/// which is how these tests tell "allowed" (anything but 401/403) from "refused" (401/403).
/// </summary>
public class AuthorizationMatrixTests : IDisposable
{
    private readonly ApiFactory _factory = new(ApiFactory.UnreachableDatabase);
    private static readonly Guid SomeId = Guid.NewGuid();

    public void Dispose() => _factory.Dispose();

    private sealed record Operation(string Method, string Url, object? Body, string[] AllowedRoles)
    {
        public override string ToString() => $"{Method} {Url}";
    }

    private static readonly string[] StaffAndAdmin = [Roles.Staff, Roles.Admin];
    private static readonly string[] Everyone = [Roles.Admin, Roles.Staff, Roles.Customer];

    private static readonly Operation[] ProtectedOperations =
    [
        new("POST", "/api/v1/vehicles", new { registrationNumber = "ABC-123", make = "A", model = "B", year = 2020, vehicleType = "Car", dailyRate = 10 }, StaffAndAdmin),
        new("POST", "/api/v1/customers", new { customerNumber = "C1", name = "Alice" }, StaffAndAdmin),
        new("GET", $"/api/v1/customers/{SomeId}", null, StaffAndAdmin),
        new("GET", "/api/v1/customers/by-number/C1", null, StaffAndAdmin),
        new("POST", "/api/v1/rentals", new { vehicleId = SomeId, customerId = SomeId, rentalDays = 3 }, StaffAndAdmin),
        new("GET", "/api/v1/rentals", null, StaffAndAdmin),
        new("POST", $"/api/v1/rentals/{SomeId}/return", null, StaffAndAdmin),
        new("GET", $"/api/v1/rentals/{SomeId}", null, Everyone),
        new("POST", "/api/v1/admin/staff", new { email = "x@example.test", password = "irrelevant" }, [Roles.Admin]),
        new("GET", "/api/v1/me", null, Everyone),
        new("GET", "/api/v1/me/customer", null, [Roles.Customer]),
        new("GET", "/api/v1/me/rentals", null, [Roles.Customer]),
        new("POST", "/api/v1/reservations", new { customerId = SomeId, vehicleId = SomeId, startDate = "2030-01-10", endDate = "2030-01-12" }, StaffAndAdmin),
        new("GET", "/api/v1/reservations", null, StaffAndAdmin),
        new("GET", $"/api/v1/reservations/{SomeId}", null, StaffAndAdmin),
        new("POST", $"/api/v1/reservations/{SomeId}/cancel", null, StaffAndAdmin),
        new("POST", $"/api/v1/reservations/{SomeId}/pickup", null, StaffAndAdmin),
        new("POST", "/api/v1/me/reservations", new { vehicleId = SomeId, startDate = "2030-01-10", endDate = "2030-01-12" }, [Roles.Customer]),
        new("GET", "/api/v1/me/reservations", null, [Roles.Customer]),
        new("GET", $"/api/v1/me/reservations/{SomeId}", null, [Roles.Customer]),
        new("POST", $"/api/v1/me/reservations/{SomeId}/cancel", null, [Roles.Customer])
    ];

    public static IEnumerable<object[]> OperationData() => Enumerable.Range(0, ProtectedOperations.Length).Select(i => new object[] { i });

    private async Task<HttpStatusCode> SendAsync(Operation operation, HttpClient client)
    {
        var request = new HttpRequestMessage(new HttpMethod(operation.Method), operation.Url);

        if (operation.Body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(operation.Body, options: Json.Options);
        }

        return (await client.SendAsync(request)).StatusCode;
    }

    private HttpClient ClientFor(string role) => _factory.CreateClientAs(role, customerId: Guid.NewGuid());

    // ----- The matrix, one operation at a time -----

    [Theory]
    [MemberData(nameof(OperationData))]
    public async Task WithoutAToken_ProtectedOperations_Return401(int operationIndex)
    {
        var operation = ProtectedOperations[operationIndex];
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(operation.Method), operation.Url));

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(OperationData))]
    public async Task RolesThatAreNotAllowed_Get403(int operationIndex)
    {
        var operation = ProtectedOperations[operationIndex];
        foreach (string role in Roles.All.Except(operation.AllowedRoles))
        {
            using var client = ClientFor(role);

            Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(operation, client));
        }
    }

    [Theory]
    [MemberData(nameof(OperationData))]
    public async Task RolesThatAreAllowed_GetPastAuthorization(int operationIndex)
    {
        var operation = ProtectedOperations[operationIndex];
        foreach (string role in operation.AllowedRoles)
        {
            using var client = ClientFor(role);

            HttpStatusCode status = await SendAsync(operation, client);

            Assert.NotEqual(HttpStatusCode.Unauthorized, status);
            Assert.NotEqual(HttpStatusCode.Forbidden, status);
        }
    }

    [Fact]
    public async Task ARefusedRequest_IsProblemDetails_WithNoAuthenticationInternals()
    {
        using var customer = ClientFor(Roles.Customer);
        using var anonymous = _factory.CreateClient();

        var forbidden = await customer.GetAsync("/api/v1/rentals");
        var unauthorized = await anonymous.GetAsync("/api/v1/rentals");

        var forbiddenBody = await forbidden.AssertProblemAsync(HttpStatusCode.Forbidden);
        var unauthorizedBody = await unauthorized.AssertProblemAsync(HttpStatusCode.Unauthorized);
        foreach (string text in new[] { forbiddenBody.GetRawText(), unauthorizedBody.GetRawText() })
        {
            Assert.DoesNotContain("IDX", text);          // token-library error codes
            Assert.DoesNotContain("policy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Staff", text);        // does not even name the roles that would be allowed
            Assert.DoesNotContain(_factory.SigningKey, text);
        }
    }

    [Fact]
    public async Task ACustomerTokenWithoutACustomerId_CannotUseSelfServiceEndpoints()
    {
        // A Customer-role token that lacks the customer link is not enough for "my" data.
        string token = TestTokens.Create(_factory.SigningKey, [Roles.Customer]);
        using var client = _factory.CreateClientWithToken(token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me/customer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me/rentals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me/reservations")).StatusCode);
    }

    [Fact]
    public async Task ATokenWithNoKnownRole_IsRefusedOnEveryRoleRestrictedOperation()
    {
        string token = TestTokens.Create(_factory.SigningKey, ["Superuser"]);
        using var client = _factory.CreateClientWithToken(token);

        foreach (var operation in ProtectedOperations.Where(o => o.AllowedRoles.Length < Everyone.Length))
        {
            Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(operation, client));
        }
    }

    // ----- Public endpoints -----

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health")]
    public async Task HealthEndpoints_NeedNoToken(string url)
    {
        using var anonymous = _factory.CreateClient();

        var status = (await anonymous.GetAsync(url)).StatusCode;

        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEqual(HttpStatusCode.Forbidden, status);
    }

    [Theory]
    [InlineData("/api/v1/vehicles")]
    [InlineData("/api/v1/vehicles/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/v1/vehicles/by-registration/ABC-123")]
    [InlineData("/api/v1/vehicles/availability?startDate=2030-01-10&endDate=2030-01-12")]
    [InlineData("/api/v1/vehicles/00000000-0000-0000-0000-000000000001/quote?startDate=2030-01-10&endDate=2030-01-12")]
    public async Task VehicleBrowsing_NeedsNoToken(string url)
    {
        using var anonymous = _factory.CreateClient();

        var status = (await anonymous.GetAsync(url)).StatusCode;

        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEqual(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task AnUnknownPath_DoesNotRevealItsExistence_ToAnAnonymousCaller()
    {
        using var anonymous = _factory.CreateClient();

        var unknown = await anonymous.GetAsync("/api/v1/definitely-not-a-route");
        var real = await anonymous.GetAsync("/api/v1/rentals");

        // Default-deny: both are 401 for someone who is not signed in.
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(real.StatusCode, unknown.StatusCode);
    }

    // ----- Every endpoint is classified: nothing is open by accident -----

    [Fact]
    public void EveryMappedEndpoint_IsEitherDeliberatelyAnonymous_OrProtected_AndMatchesTheDocumentedMatrix()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"];
                string route = "/" + (endpoint.RoutePattern.RawText ?? "").Trim('/');

                return methods.Select(method => (Key: $"{method} {route}", Endpoint: (Endpoint)endpoint));
            })
            .ToList();

        var anonymous = endpoints
            .Where(e => e.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.Key)
            .Order()
            .ToList();

        // Documentation routes exist only in Development, and are anonymous there by design.
        var anonymousApi = anonymous.Where(k => !k.Contains("/openapi") && !k.Contains("/scalar")).ToList();

        Assert.Equal(
            new[]
            {
                "ANY /health",          // health checks answer any HTTP method
                "ANY /health/live",
                "GET /api/v1/vehicles",
                "GET /api/v1/vehicles/availability",   // like browsing: reveals only which vehicles are free
                "GET /api/v1/vehicles/by-registration/{registrationNumber}",
                "GET /api/v1/vehicles/{id}",
                "GET /api/v1/vehicles/{id}/quote",
                "POST /api/v1/auth/login",
                "POST /api/v1/auth/register",
                "POST /api/v1/auth/session",           // signs in; the anti-CSRF header is its check
                "DELETE /api/v1/auth/session"
            }.Order(),
            anonymousApi);

        // Everything else must carry an explicit authorization requirement, and match the matrix above.
        var protectedKeys = endpoints
            .Where(e => e.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(e => e.Key)
            .Where(k => !k.Contains("/openapi") && !k.Contains("/scalar"))
            .Order()
            .ToList();

        var expectedProtected = ProtectedOperations
            .Select(o => $"{o.Method} {RouteTemplate(o.Url)}")
            .Order()
            .ToList();

        Assert.Equal(expectedProtected, protectedKeys);
    }

    [Fact]
    public void ProtectedEndpoints_NameAPolicy_Rather_Than_RawRoles()
    {
        var withRoles = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Where(a => !string.IsNullOrEmpty(a.Roles))
            .ToList();

        Assert.Empty(withRoles);
    }

    /// <summary>Turns a concrete URL from the table into its route template.</summary>
    private static string RouteTemplate(string url) => url
        .Split('?')[0]
        .Replace(SomeId.ToString(), "{id}")
        .Replace("/by-number/C1", "/by-number/{customerNumber}");
}
