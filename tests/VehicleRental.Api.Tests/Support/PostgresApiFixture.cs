using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VehicleRental.Application.Accounts;
using VehicleRental.Application.Security;
using VehicleRental.Infrastructure.Identity;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Api.Tests.Support;

/// <summary>
/// Creates one throw-away database for the whole test run, applies the real migrations to it, starts the
/// API against it, and drops the database afterwards. Isolation between tests comes from emptying the
/// tables before each test.
/// </summary>
public sealed class PostgresApiFixture : IAsyncLifetime
{
    private const string DatabasePrefix = "vehiclerental_api_it_";

    private readonly List<string> _databases = new();
    private string? _serverConnectionString;

    internal ApiFactory Factory { get; private set; } = null!;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _serverConnectionString = TestDatabase.ServerConnectionString;

        if (_serverConnectionString is null)
        {
            return; // every database test is skipped
        }

        ConnectionString = await CreateDatabaseAsync(applyMigrations: true);
        Factory = new ApiFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_serverConnectionString is null)
        {
            return;
        }

        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(WithDatabase(_serverConnectionString, "postgres"));
        await admin.OpenAsync();

        foreach (string database in _databases.Where(d => d.StartsWith(DatabasePrefix)))
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Creates another uniquely named database, optionally without any schema.</summary>
    public async Task<string> CreateDatabaseAsync(bool applyMigrations)
    {
        string name = DatabasePrefix + Guid.NewGuid().ToString("N");
        _databases.Add(name);

        await using (var admin = new NpgsqlConnection(WithDatabase(_serverConnectionString!, "postgres")))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        string connectionString = WithDatabase(_serverConnectionString!, name);

        if (applyMigrations)
        {
            await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(connectionString));
            await context.Database.MigrateAsync();
        }

        return connectionString;
    }

    /// <summary>Empties every business and account table of the main test database (the seeded roles stay).</summary>
    public async Task ResetAsync()
    {
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(ConnectionString));
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Rentals\", \"Customers\", \"Vehicles\", \"Users\" CASCADE");
    }

    // ----- Real accounts, signed in through the real login endpoint -----

    /// <summary>A client with no credentials.</summary>
    internal HttpClient CreateAnonymousClient() => Factory.CreateClient();

    /// <summary>Creates a staff account and returns a client signed in as it.</summary>
    internal async Task<HttpClient> CreateStaffClientAsync(string? email = null)
    {
        string address = email ?? $"staff-{Guid.NewGuid():N}@example.test";
        string password = ApiFactory.NewPassword();

        using IServiceScope scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateStaffAsync(address, password);

        return await SignInAsync(address, password);
    }

    /// <summary>Creates an admin account (there is deliberately no public way to) and returns a client signed in as it.</summary>
    internal async Task<HttpClient> CreateAdminClientAsync()
    {
        string address = $"admin-{Guid.NewGuid():N}@example.test";
        string password = ApiFactory.NewPassword();

        using (IServiceScope scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = new ApplicationUser { Id = Guid.CreateVersion7(), UserName = address, Email = address };

            Assert.True((await users.CreateAsync(admin, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, Roles.Admin)).Succeeded);
        }

        return await SignInAsync(address, password);
    }

    /// <summary>Registers a customer account through the public endpoint and returns a client signed in as it.</summary>
    internal async Task<CustomerSession> CreateCustomerClientAsync(string name = "Casey Customer")
    {
        string address = $"customer-{Guid.NewGuid():N}@example.test";
        string password = ApiFactory.NewPassword();

        using HttpClient anonymous = CreateAnonymousClient();
        var registered = await anonymous.PostJsonAsync("/api/v1/auth/register", new { email = address, password, name });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        UserDto user = await registered.ReadAsync<UserDto>();

        return new CustomerSession(await SignInAsync(address, password), user, address, password);
    }

    internal async Task<HttpClient> SignInAsync(string email, string password)
    {
        using HttpClient anonymous = CreateAnonymousClient();
        var response = await anonymous.PostJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string token = (await response.ReadJsonAsync()).GetProperty("accessToken").GetString()!;

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
}

/// <summary>A signed-in customer account and the credentials it was created with.</summary>
internal sealed record CustomerSession(HttpClient Client, UserDto User, string Email, string Password);

[CollectionDefinition(Name)]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "PostgreSQL API";
}

/// <summary>
/// Base class for API tests that need the database: they share one database and run one at a time, each
/// starting empty. <see cref="Client"/> is a staff account, the account that runs the rental desk, because
/// the business flows these tests exercise are staff operations. Tests of other roles create their own clients.
/// </summary>
[Collection(PostgresApiCollection.Name)]
public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiTestBase(PostgresApiFixture api) => Api = api;

    internal PostgresApiFixture Api { get; }

    /// <summary>A client signed in as a staff member.</summary>
    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsConfigured)
        {
            return;
        }

        await Api.ResetAsync();
        Client = await Api.CreateStaffClientAsync();
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();

        return Task.CompletedTask;
    }
}
