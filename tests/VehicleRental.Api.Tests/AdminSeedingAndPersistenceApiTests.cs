using System.Net;
using Microsoft.EntityFrameworkCore;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application.Accounts;
using VehicleRental.Application.Security;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Api.Tests;

/// <summary>
/// The initial admin account (created only from configuration) and the persistence of accounts across
/// application restarts. Each test starts its own application instance(s) against its own fresh database.
/// </summary>
public class AdminSeedingAndPersistenceApiTests : ApiTestBase
{
    public AdminSeedingAndPersistenceApiTests(PostgresApiFixture api) : base(api)
    {
    }

    private static Dictionary<string, string?> SeedSettings(string email, string password) => new()
    {
        ["Seed:Admin:Email"] = email,
        ["Seed:Admin:Password"] = password
    };

    private static async Task<HttpResponseMessage> TryLoginAsync(HttpClient client, string email, string password) =>
        await client.PostJsonAsync("/api/v1/auth/login", new { email, password });

    /// <summary>The admin is created by a background service shortly after startup, so wait for it.</summary>
    private static async Task<bool> WaitForLoginAsync(HttpClient client, string email, string password)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if ((await TryLoginAsync(client, email, password)).StatusCode == HttpStatusCode.OK)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private async Task<int> UserCountAsync(string connectionString)
    {
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(connectionString));

        return await context.Users.CountAsync();
    }

    // ----- Seeding -----

    [DatabaseFact]
    public async Task WithAdminCredentialsInConfiguration_TheApiCreatesTheAdmin_WhoCanSignInAndUseAdminEndpoints()
    {
        string connection = await Api.CreateDatabaseAsync(applyMigrations: true);
        string email = $"root-{Guid.NewGuid():N}@example.test";
        string password = ApiFactory.NewPassword();

        await using var factory = new ApiFactory(connection, extraSettings: SeedSettings(email, password));
        using var client = factory.CreateClient();

        Assert.True(await WaitForLoginAsync(client, email, password), "The configured admin should be able to sign in.");

        var login = await (await TryLoginAsync(client, email, password)).ReadJsonAsync();
        Assert.Equal(new[] { Roles.Admin }, login.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        using var admin = factory.CreateClientWithToken(login.GetProperty("accessToken").GetString()!);
        var staff = await admin.PostJsonAsync("/api/v1/admin/staff",
            new { email = $"s-{Guid.NewGuid():N}@example.test", password = ApiFactory.NewPassword() });
        Assert.Equal(HttpStatusCode.Created, staff.StatusCode);
    }

    [DatabaseFact]
    public async Task WithoutAdminCredentials_NoAdminIsCreated_AndTheApiStillRuns()
    {
        string connection = await Api.CreateDatabaseAsync(applyMigrations: true);

        await using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();
        await Task.Delay(1000); // long enough for the startup tasks to have run

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(0, await UserCountAsync(connection));
    }

    [DatabaseFact]
    public async Task ARestart_NeverDuplicatesTheAdmin_OrChangesItsPassword()
    {
        string connection = await Api.CreateDatabaseAsync(applyMigrations: true);
        string email = $"root-{Guid.NewGuid():N}@example.test";
        string original = ApiFactory.NewPassword();

        await using (var first = new ApiFactory(connection, extraSettings: SeedSettings(email, original)))
        {
            using var client = first.CreateClient();
            Assert.True(await WaitForLoginAsync(client, email, original));
        }

        // The second start is configured with a different password; it must leave the account alone.
        string different = ApiFactory.NewPassword();
        await using var second = new ApiFactory(connection, extraSettings: SeedSettings(email, different));
        using var secondClient = second.CreateClient();
        await Task.Delay(1000);

        Assert.Equal(HttpStatusCode.OK, (await TryLoginAsync(secondClient, email, original)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TryLoginAsync(secondClient, email, different)).StatusCode);
        Assert.Equal(1, await UserCountAsync(connection));
    }

    [DatabaseFact]
    public async Task AWeakAdminPassword_IsRefused_TheApiStillStarts_AndTheLogsNeverContainIt()
    {
        string connection = await Api.CreateDatabaseAsync(applyMigrations: true);
        const string weak = "weakpw";
        string email = $"root-{Guid.NewGuid():N}@example.test";

        await using var factory = new ApiFactory(connection, extraSettings: SeedSettings(email, weak));
        using var client = factory.CreateClient();
        await Task.Delay(1000);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(0, await UserCountAsync(connection));
        string[] logs;
        lock (factory.Logs)
        {
            logs = factory.Logs.ToArray();
        }

        Assert.DoesNotContain(logs, l => l.Contains(weak));
    }

    [DatabaseFact]
    public async Task WhenTheDatabaseIsNotMigrated_SeedingIsSkipped_NotACrash()
    {
        string unmigrated = await Api.CreateDatabaseAsync(applyMigrations: false);
        string password = ApiFactory.NewPassword();

        await using var factory = new ApiFactory(unmigrated,
            extraSettings: SeedSettings($"root-{Guid.NewGuid():N}@example.test", password));
        using var client = factory.CreateClient();
        await Task.Delay(1000);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health")).StatusCode);
        lock (factory.Logs)
        {
            Assert.Contains(factory.Logs, l => l.Contains("Skipping initial admin creation"));
            Assert.DoesNotContain(factory.Logs, l => l.Contains(password));
        }
    }

    // ----- Persistence across restarts -----

    [DatabaseFact]
    public async Task Accounts_Survive_ARestart_AndLoginStillWorks()
    {
        var customer = await Api.CreateCustomerClientAsync("Persistent Pat");
        using var admin = await Api.CreateAdminClientAsync();
        string staffEmail = $"s-{Guid.NewGuid():N}@example.test";
        string staffPassword = ApiFactory.NewPassword();
        (await admin.PostJsonAsync("/api/v1/admin/staff", new { email = staffEmail, password = staffPassword })).EnsureSuccessStatusCode();

        // A new application instance with a different signing key, as after a redeploy: old tokens stop
        // working, but the accounts are in the database, so signing in again works.
        await using var restarted = new ApiFactory(Api.ConnectionString);
        using var client = restarted.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await TryLoginAsync(client, customer.Email, customer.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TryLoginAsync(client, staffEmail, staffPassword)).StatusCode);
    }

    [DatabaseFact]
    public async Task ATokenFromBeforeARestartWithADifferentKey_IsRejectedByTheNewInstance()
    {
        var customer = await Api.CreateCustomerClientAsync();
        string oldToken = customer.Client.DefaultRequestHeaders.Authorization!.Parameter!;

        await using var restarted = new ApiFactory(Api.ConnectionString); // a new random key
        using var withOldToken = restarted.CreateClientWithToken(oldToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await withOldToken.GetAsync("/api/v1/me")).StatusCode);
    }

    [DatabaseFact]
    public async Task ATokenSurvivesARestart_WhenTheSigningKeyIsTheSame()
    {
        var customer = await Api.CreateCustomerClientAsync();
        string token = customer.Client.DefaultRequestHeaders.Authorization!.Parameter!;

        await using var restarted = new ApiFactory(Api.ConnectionString, signingKey: Api.Factory.SigningKey);
        using var client = restarted.CreateClientWithToken(token);

        var me = await (await client.GetAsync("/api/v1/me")).ReadAsync<UserDto>();
        Assert.Equal(customer.User.Id, me.Id);
    }
}
