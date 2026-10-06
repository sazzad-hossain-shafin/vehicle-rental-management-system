using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public HttpClient Client { get; private set; } = null!;

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
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (_serverConnectionString is null)
        {
            return;
        }

        Client?.Dispose();

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

    /// <summary>Empties every table of the main test database.</summary>
    public async Task ResetAsync()
    {
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(ConnectionString));
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Rentals\", \"Customers\", \"Vehicles\"");
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "PostgreSQL API";
}

/// <summary>
/// Base class for API tests that need the database: they share one database and run one at a time,
/// each starting empty.
/// </summary>
[Collection(PostgresApiCollection.Name)]
public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiTestBase(PostgresApiFixture api) => Api = api;

    protected PostgresApiFixture Api { get; }

    protected HttpClient Client => Api.Client;

    public Task InitializeAsync() => TestDatabase.IsConfigured ? Api.ResetAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;
}
