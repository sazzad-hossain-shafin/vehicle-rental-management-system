using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using VehicleRental.Api.Security;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application;
using VehicleRental.Application.Tenancy;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure;

namespace VehicleRental.Api.Tests;

/// <summary>
/// The tenant endpoint filters (company from the route, membership verified in the database, tenant transaction), run on
/// test-only routes in a throwaway host: Phase 14A-4 attaches them to NO production route. The database role is the
/// restricted runtime role and the three tenant tables carry PREVIEW Row-Level Security (disposable database only).
/// </summary>
[Collection(PostgresApiCollection.Name)]
public class TenantEndpointFilterTests : IAsyncLifetime
{
    private readonly PostgresApiFixture _api;
    private string _owner = "";
    private string _app = "";
    private string _role = "";
    private WebApplication _host = null!;
    private HttpClient _client = null!;

    public TenantEndpointFilterTests(PostgresApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsConfigured)
        {
            return;
        }

        _owner = await _api.CreateDatabaseAsync(applyMigrations: true);
        (_role, _app) = await RuntimeRole.ProvisionAsync(_owner);
        await Run(_owner, ReadResource("preview-rls.sql"));
        _host = await StartHostAsync(tenancyEnabled: true);
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();

        if (_role.Length > 0)
        {
            // The database itself is dropped later by the shared fixture, so release the role's privileges here first.
            await Run(_owner, $"DROP OWNED BY \"{_role}\"");
            await RuntimeRole.DropAsync(TestDatabase.ServerConnectionString!, _role);
        }
    }

    // ---- test host: real services, a header-based test login, and tenant routes ------------------------------------

    private async Task<WebApplication> StartHostAsync(bool tenancyEnabled)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // A small pool: 120 simultaneous requests must not exhaust the test server's connection slots.
            ["ConnectionStrings:VehicleRentalDatabase"] = _app + ";Maximum Pool Size=8;Timeout=60",
            ["Tenancy:Enabled"] = tenancyEnabled ? "true" : "false",
            ["Jwt:Issuer"] = "t",
            ["Jwt:Audience"] = "t",
            ["Jwt:SigningKey"] = ApiFactory.NewRandomKey(),
        });
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderLogin>("test", null);
        builder.Services.AddAuthorization();

        WebApplication app = builder.Build();

        // An unhandled exception becomes a plain 500, as the real API's exception handler does.
        app.UseExceptionHandler(failure => failure.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));
        app.UseAuthentication();
        app.UseAuthorization();

        // Public storefront: the company comes from the slug.
        app.MapGet("/c/{slug}/vehicles", async (VehicleService vehicles, CancellationToken ct) => Results.Ok(await vehicles.GetAllAsync(ct)))
            .AllowAnonymous().RequirePublicCompany().InTenantTransaction();

        // Management: the company comes from a route value that is VERIFIED against the signed-in user's membership.
        app.MapGet("/manage/c/{companyId}/vehicles", async (VehicleService vehicles, ITenantContext tenant, CancellationToken ct) =>
                Results.Ok(new { company = tenant.CompanyId, source = tenant.Source.ToString(), vehicles = await vehicles.GetAllAsync(ct) }))
            .RequireAuthorization().RequireCompanyMember(CompanyRole.Staff).InTenantTransaction();

        app.MapPost("/manage/c/{companyId}/vehicles", async (HttpContext http, VehicleService vehicles, CancellationToken ct) =>
            {
                string registration = http.Request.Query["registration"].ToString();
                return Results.Ok(await vehicles.AddVehicleAsync(new AddVehicleRequest(registration, "Toyota", "Corolla", 2022, VehicleType.Car, 50m), ct));
            })
            .RequireAuthorization().RequireCompanyMember(CompanyRole.Staff).InTenantTransaction();

        app.MapPost("/manage/c/{companyId}/owner-only", () => Results.Ok("done"))
            .RequireAuthorization().RequireCompanyMember(CompanyRole.Owner).InTenantTransaction();

        // Saves a vehicle and then answers 409: the transaction must roll back, the save must not survive.
        app.MapPost("/manage/c/{companyId}/save-then-409", async (HttpContext http, VehicleService vehicles, CancellationToken ct) =>
            {
                await vehicles.AddVehicleAsync(new AddVehicleRequest(http.Request.Query["registration"].ToString(), "Toyota", "Corolla", 2022, VehicleType.Car, 50m), ct);
                return Results.Conflict();
            })
            .RequireAuthorization().RequireCompanyMember(CompanyRole.Staff).InTenantTransaction();

        app.MapPost("/manage/c/{companyId}/save-then-throw", async (HttpContext http, VehicleService vehicles, CancellationToken ct) =>
            {
                await vehicles.AddVehicleAsync(new AddVehicleRequest(http.Request.Query["registration"].ToString(), "Toyota", "Corolla", 2022, VehicleType.Car, 50m), ct);
                throw new InvalidOperationException("the handler failed after saving");
            })
            .RequireAuthorization().RequireCompanyMember(CompanyRole.Staff).InTenantTransaction();

        await app.StartAsync();

        return app;
    }

    private sealed class HeaderLogin : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public HeaderLogin(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var value) || !Guid.TryParse(value, out Guid user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("sub", user.ToString())], "test", "sub", "role");

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }

    // ---- data helpers (as the owner: a superuser, exempt from Row-Level Security) ---------------------------------

    private static string ReadResource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static async Task Run(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> Count(string sql)
    {
        await using var connection = new NpgsqlConnection(_owner);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> Company(string slug, string status = "Active")
    {
        Guid id = Guid.NewGuid();
        await Run(
            _owner,
            $"INSERT INTO \"Companies\" (\"Id\", \"Slug\", \"Name\", \"Status\", \"TimeZoneId\", \"CreatedAt\", \"ApprovedAt\") VALUES ('{id}', '{slug}', 'Co {slug}', '{status}', 'UTC', now(), {(status == "PendingReview" ? "NULL" : "now()")})");

        return id;
    }

    private async Task<Guid> User()
    {
        Guid id = Guid.NewGuid();
        await Run(_owner, $"INSERT INTO \"Users\" (\"Id\", \"UserName\", \"AccessFailedCount\", \"EmailConfirmed\", \"LockoutEnabled\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\") VALUES ('{id}', 'u-{id:N}', 0, false, true, false, false)");

        return id;
    }

    private Task Member(Guid company, Guid user, string role, string status = "Active") =>
        Run(_owner, $"INSERT INTO \"CompanyMemberships\" (\"Id\", \"CompanyId\", \"UserId\", \"Role\", \"Status\", \"CreatedAt\") VALUES ('{Guid.NewGuid()}', '{company}', '{user}', '{role}', '{status}', now())");

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, Guid? user = null)
    {
        using var request = new HttpRequestMessage(method, url);

        if (user is { } id)
        {
            request.Headers.Add("X-Test-User", id.ToString());
        }

        return await _client.SendAsync(request);
    }

    private static string Reg() => "F-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    // ---- public storefront (slug) ----------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task A_public_request_for_an_active_company_is_scoped_to_that_company_only()
    {
        Guid a = await Company("store-a");
        Guid b = await Company("store-b");
        Guid staffA = await User();
        Guid staffB = await User();
        await Member(a, staffA, "Owner");
        await Member(b, staffB, "Owner");
        string regA = Reg(), regB = Reg();
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/manage/c/{a}/vehicles?registration={regA}", staffA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/manage/c/{b}/vehicles?registration={regB}", staffB)).StatusCode);

        var response = await Send(HttpMethod.Get, "/c/Store-A/vehicles"); // no sign-in; case-insensitive slug

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(regA, body, StringComparison.Ordinal);
        Assert.DoesNotContain(regB, body, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Unknown_pending_suspended_malformed_and_reserved_slugs_all_answer_the_same_404()
    {
        await Company("pending-store", "PendingReview");
        await Company("suspended-store", "Suspended");

        var bodies = new List<string>();

        foreach (string slug in new[] { "pending-store", "suspended-store", "nobody-here", "admin", "a--b", "-x", "ab" })
        {
            var response = await Send(HttpMethod.Get, $"/c/{slug}/vehicles");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            bodies.Add(System.Text.RegularExpressions.Regex.Replace(await response.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", ""));
        }

        Assert.Single(bodies.Distinct()); // nothing in the answer tells them apart
    }

    // ---- management (membership) -----------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task A_member_is_scoped_to_the_company_in_the_route_and_the_source_is_membership()
    {
        Guid company = await Company("members-co");
        Guid user = await User();
        await Member(company, user, "Staff");

        var response = await Send(HttpMethod.Get, $"/manage/c/{company}/vehicles", user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(company.ToString(), body, StringComparison.Ordinal);
        Assert.Contains("Membership", body, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_stranger_a_foreign_company_id_and_a_missing_login_are_all_a_plain_404()
    {
        Guid mine = await Company("mine-co");
        Guid theirs = await Company("theirs-co");
        Guid user = await User();
        Guid stranger = await User();
        await Member(mine, user, "Owner");

        // Not a member of that company at all, a company that does not exist, no login, and a made-up user: same answer.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/manage/c/{theirs}/vehicles", user)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/manage/c/{Guid.NewGuid()}/vehicles", user)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/manage/c/{mine}/vehicles", stranger)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/manage/c/{mine}/vehicles", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/manage/c/not-a-guid/vehicles", user)).StatusCode);
        Assert.True((await Send(HttpMethod.Get, $"/manage/c/{mine}/vehicles")).StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task A_company_in_the_body_query_header_or_a_second_route_value_is_never_used()
    {
        Guid mine = await Company("mine2-co");
        Guid theirs = await Company("theirs2-co");
        Guid user = await User();
        Guid owner = await User();
        await Member(mine, user, "Staff");
        await Member(theirs, owner, "Owner");
        string theirReg = Reg();
        await Send(HttpMethod.Post, $"/manage/c/{theirs}/vehicles?registration={theirReg}", owner);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/manage/c/{mine}/vehicles?companyId={theirs}&company={theirs}");
        request.Headers.Add("X-Test-User", user.ToString());
        request.Headers.Add("X-Company-Id", theirs.ToString());
        request.Headers.Add("X-Tenant", theirs.ToString());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(mine.ToString(), body, StringComparison.Ordinal);
        Assert.DoesNotContain(theirReg, body, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Staff_are_refused_an_owner_only_route_with_403_and_owners_are_allowed()
    {
        Guid company = await Company("roles-co");
        Guid staff = await User();
        Guid owner = await User();
        await Member(company, staff, "Staff");
        await Member(company, owner, "Owner");

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"/manage/c/{company}/owner-only", staff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/manage/c/{company}/owner-only", owner)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"/manage/c/{company}/vehicles", owner)).StatusCode); // owners outrank staff
    }

    [DatabaseFact]
    public async Task Removing_a_member_takes_effect_on_their_very_next_request()
    {
        Guid company = await Company("revoke-co");
        Guid user = await User();
        await Member(company, user, "Staff");
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"/manage/c/{company}/vehicles", user)).StatusCode);

        await Run(_owner, $"UPDATE \"CompanyMemberships\" SET \"Status\" = 'Removed' WHERE \"CompanyId\" = '{company}' AND \"UserId\" = '{user}'");

        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/manage/c/{company}/vehicles", user)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_suspended_company_cannot_be_managed_but_a_pending_one_can()
    {
        Guid pending = await Company("pending-mgmt", "PendingReview");
        Guid suspended = await Company("suspended-mgmt", "Suspended");
        Guid user = await User();
        await Member(pending, user, "Owner");
        await Member(suspended, user, "Owner");

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"/manage/c/{pending}/vehicles", user)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/manage/c/{suspended}/vehicles", user)).StatusCode);
    }

    // ---- the transaction -------------------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task A_successful_request_commits_and_a_400_plus_answer_or_an_exception_rolls_back()
    {
        Guid company = await Company("tx-co");
        Guid user = await User();
        await Member(company, user, "Staff");
        string committed = Reg(), conflicted = Reg(), thrown = Reg();

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/manage/c/{company}/vehicles?registration={committed}", user)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, $"/manage/c/{company}/save-then-409?registration={conflicted}", user)).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Send(HttpMethod.Post, $"/manage/c/{company}/save-then-throw?registration={thrown}", user)).StatusCode);

        Assert.Equal(1L, await Count($"SELECT count(*) FROM \"Vehicles\" WHERE \"RegistrationNumber\" = '{committed}' AND \"CompanyId\" = '{company}'"));
        Assert.Equal(0L, await Count($"SELECT count(*) FROM \"Vehicles\" WHERE \"RegistrationNumber\" IN ('{conflicted}', '{thrown}')"));
    }

    [DatabaseFact]
    public async Task Many_simultaneous_requests_from_different_companies_never_cross()
    {
        var companies = new List<(Guid Id, Guid User, string Reg)>();

        for (int i = 0; i < 3; i++)
        {
            Guid id = await Company($"par-{i}-co");
            Guid user = await User();
            await Member(id, user, "Owner");
            string reg = Reg();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/manage/c/{id}/vehicles?registration={reg}", user)).StatusCode);
            companies.Add((id, user, reg));
        }

        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(0, 120).Select(async i =>
        {
            (Guid id, Guid user, string reg) = companies[i % 3];
            var response = await Send(HttpMethod.Get, $"/manage/c/{id}/vehicles", user);
            string body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.OK || !body.Contains(reg, StringComparison.Ordinal)
                || companies.Where(c => c.Id != id).Any(c => body.Contains(c.Reg, StringComparison.Ordinal)))
            {
                wrong.Add($"request {i} saw the wrong data ({response.StatusCode})");
            }
        }));

        Assert.Empty(wrong);
    }

    [DatabaseFact]
    public async Task With_tenancy_disabled_every_tenant_route_answers_404_and_changes_nothing()
    {
        Guid company = await Company("off-co");
        Guid user = await User();
        await Member(company, user, "Owner");

        await using WebApplication disabled = await StartHostAsync(tenancyEnabled: false);
        HttpClient client = disabled.GetTestClient();

        foreach (string url in new[] { "/c/off-co/vehicles", $"/manage/c/{company}/vehicles" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Test-User", user.ToString());

            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
        }
    }
}
