using System.Net;
using System.Text.Json;
using Npgsql;
using VehicleRental.Api.Tests.Support;

namespace VehicleRental.Api.Tests;

/// <summary>
/// Tenancy:DatabaseRoleCheck at startup and in /health. The test server's admin role is a superuser that owns the tables
/// (every kind of unsafe); the provisioned runtime role is the safe one. Nothing may leak a role name, a password or a
/// connection string.
/// </summary>
[Collection(PostgresApiCollection.Name)]
public class DatabaseRoleCheckApiTests
{
    private static readonly Dictionary<string, string?> Enforce = new() { ["Tenancy:DatabaseRoleCheck"] = "Enforce" };
    private static readonly Dictionary<string, string?> Warn = new() { ["Tenancy:DatabaseRoleCheck"] = "Warn" };
    private static readonly Dictionary<string, string?> Off = new() { ["Tenancy:DatabaseRoleCheck"] = "Off" };

    private readonly PostgresApiFixture _api;

    public DatabaseRoleCheckApiTests(PostgresApiFixture api) => _api = api;

    private async Task<(string Owner, string Runtime, string Role)> NewDatabaseAsync()
    {
        string owner = await _api.CreateDatabaseAsync(applyMigrations: true);
        (string role, string runtime) = await RuntimeRole.ProvisionAsync(owner);

        return (owner, runtime, role);
    }

    private static string PasswordOf(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Password!;

    private static async Task<JsonElement> RoleCheckAsync(HttpClient client, HttpStatusCode expected)
    {
        var response = await client.GetAsync("/health");
        Assert.Equal(expected, response.StatusCode);

        string text = await response.Content.ReadAsStringAsync();
        JsonElement body = JsonDocument.Parse(text).RootElement.Clone();

        return body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "database-role");
    }

    private static async Task Cleanup(string owner, string role)
    {
        NpgsqlConnection.ClearAllPools();
        await using (var connection = new NpgsqlConnection(owner))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP OWNED BY \"{role}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        await RuntimeRole.DropAsync(TestDatabase.ServerConnectionString!, role);
    }

    [DatabaseFact]
    public async Task Enforce_refuses_to_start_when_the_api_would_connect_as_a_superuser_and_leaks_nothing()
    {
        (string owner, string runtime, string role) = await NewDatabaseAsync();

        try
        {
            await using var factory = new ApiFactory(owner, extraSettings: Enforce);

            Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            string text = error.ToString();
            Assert.Contains("not safe", text, StringComparison.Ordinal);
            Assert.DoesNotContain(PasswordOf(owner), text + string.Join("\n", factory.Logs), StringComparison.Ordinal);
            Assert.DoesNotContain(PasswordOf(runtime), text + string.Join("\n", factory.Logs), StringComparison.Ordinal);
            Assert.Contains(factory.Logs, l => l.Contains("Unsafe database role", StringComparison.Ordinal));
        }
        finally
        {
            await Cleanup(owner, role);
        }
    }

    [DatabaseFact]
    public async Task Enforce_starts_with_the_restricted_runtime_role_and_health_says_the_role_is_safe_without_naming_it()
    {
        (string owner, string runtime, string role) = await NewDatabaseAsync();

        try
        {
            await using var factory = new ApiFactory(runtime, extraSettings: Enforce);
            using HttpClient client = factory.CreateClient();

            JsonElement check = await RoleCheckAsync(client, HttpStatusCode.OK);

            Assert.Equal("Healthy", check.GetProperty("status").GetString());
            Assert.Equal("The database role is safe.", check.GetProperty("description").GetString());

            string everything = await (await client.GetAsync("/health")).Content.ReadAsStringAsync() + string.Join("\n", factory.Logs);
            Assert.DoesNotContain(role, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(PasswordOf(runtime), everything, StringComparison.Ordinal);
            Assert.Contains(factory.Logs, l => l.Contains("passed the safety check", StringComparison.Ordinal));
        }
        finally
        {
            await Cleanup(owner, role);
        }
    }

    [DatabaseFact]
    public async Task Warn_starts_logs_each_problem_and_leaves_health_ready()
    {
        (string owner, string _, string role) = await NewDatabaseAsync();

        try
        {
            await using var factory = new ApiFactory(owner, extraSettings: Warn);
            using HttpClient client = factory.CreateClient();

            JsonElement check = await RoleCheckAsync(client, HttpStatusCode.OK);

            Assert.Equal("Healthy", check.GetProperty("status").GetString()); // readiness is unchanged in Warn mode
            Assert.StartsWith("Warning:", check.GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.Contains(factory.Logs, l => l.Contains("Unsafe database role", StringComparison.Ordinal) && l.Contains("superuser", StringComparison.Ordinal));
            Assert.DoesNotContain(PasswordOf(owner), string.Join("\n", factory.Logs), StringComparison.Ordinal);
        }
        finally
        {
            await Cleanup(owner, role);
        }
    }

    [DatabaseFact]
    public async Task The_default_in_Development_is_Warn_so_local_development_with_a_privileged_role_keeps_working()
    {
        (string owner, string _, string role) = await NewDatabaseAsync();

        try
        {
            await using var factory = new ApiFactory(owner);
            using HttpClient client = factory.CreateClient();

            JsonElement check = await RoleCheckAsync(client, HttpStatusCode.OK);

            Assert.StartsWith("Warning:", check.GetProperty("description").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            await Cleanup(owner, role);
        }
    }

    [DatabaseFact]
    public async Task The_default_outside_Development_is_Enforce_so_a_forgotten_setting_cannot_start_as_an_unsafe_role()
    {
        (string owner, string runtime, string role) = await NewDatabaseAsync();

        try
        {
            // No Tenancy:DatabaseRoleCheck at all, in Production, connecting as the owner (a superuser).
            await using var unsafeRole = new ApiFactory(owner, "Production");

            Exception error = Assert.ThrowsAny<Exception>(() => unsafeRole.CreateClient());

            Assert.Contains("not safe", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(PasswordOf(owner), error + string.Join("\n", unsafeRole.Logs), StringComparison.Ordinal);

            // The same default with the restricted runtime role starts, and readiness is enforced.
            await using var safeRole = new ApiFactory(runtime, "Production");
            using HttpClient client = safeRole.CreateClient();

            JsonElement check = await RoleCheckAsync(client, HttpStatusCode.OK);
            Assert.Equal("The database role is safe.", check.GetProperty("description").GetString());
        }
        finally
        {
            await Cleanup(owner, role);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Off_is_refused_outside_Development_even_with_a_safe_role(string environment)
    {
        // An unreachable database is enough: the refusal happens before any database work.
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, environment, extraSettings: Off);

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Tenancy:DatabaseRoleCheck=Off is allowed only in the Development environment", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", error.ToString(), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task An_explicit_Warn_outside_Development_starts_but_is_logged_as_a_temporary_emergency_setting()
    {
        (string owner, string _, string role) = await NewDatabaseAsync();

        try
        {
            await using var factory = new ApiFactory(owner, "Production", extraSettings: Warn);
            using HttpClient client = factory.CreateClient();

            JsonElement check = await RoleCheckAsync(client, HttpStatusCode.OK);

            Assert.StartsWith("Warning:", check.GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.Contains(factory.Logs, l => l.Contains("temporary emergency setting", StringComparison.Ordinal));
        }
        finally
        {
            await Cleanup(owner, role);
        }
    }

    [DatabaseFact]
    public async Task Off_runs_no_check_and_logs_no_warning()
    {
        (string owner, string _, string role) = await NewDatabaseAsync();

        try
        {
            await using var factory = new ApiFactory(owner, extraSettings: Off);
            using HttpClient client = factory.CreateClient();

            JsonElement check = await RoleCheckAsync(client, HttpStatusCode.OK);

            Assert.Equal("The database role check is switched off.", check.GetProperty("description").GetString());
            Assert.DoesNotContain(factory.Logs, l => l.Contains("Unsafe database role", StringComparison.Ordinal));
        }
        finally
        {
            await Cleanup(owner, role);
        }
    }

    [DatabaseFact]
    public async Task Enforce_with_an_unreachable_database_starts_degraded_and_reports_unhealthy_without_connection_details()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, extraSettings: Enforce);
        using HttpClient client = factory.CreateClient();

        JsonElement check = await RoleCheckAsync(client, HttpStatusCode.ServiceUnavailable);

        Assert.Equal("Unhealthy", check.GetProperty("status").GetString());
        Assert.Equal("The database role could not be checked.", check.GetProperty("description").GetString());
        Assert.DoesNotContain("127.0.0.1", string.Join("\n", factory.Logs), StringComparison.Ordinal);
    }
}
