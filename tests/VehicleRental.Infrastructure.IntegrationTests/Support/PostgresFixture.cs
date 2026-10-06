using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests.Support;

/// <summary>
/// Creates one throw-away database for the whole test run, applies the real migrations to it,
/// and drops it afterwards. Isolation between tests comes from emptying the tables before each test.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DatabasePrefix = "vehiclerental_it_";

    private string? _serverConnectionString;
    private string? _databaseName;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _serverConnectionString = TestDatabase.ServerConnectionString;

        if (_serverConnectionString is null)
        {
            return; // every database test is skipped
        }

        _databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");

        // Administrative work happens on the server's default "postgres" database.
        await using (var admin = new NpgsqlConnection(WithDatabase(_serverConnectionString, "postgres")))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = WithDatabase(_serverConnectionString, _databaseName);

        await using VehicleRentalDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        // Drops only the database this fixture created (the name check is a guard against mistakes).
        if (_serverConnectionString is null || _databaseName is null || !_databaseName.StartsWith(DatabasePrefix))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(WithDatabase(_serverConnectionString, "postgres"));
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    public VehicleRentalDbContext CreateContext() =>
        new(VehicleRentalDbContextOptions.Create(ConnectionString));

    public PersistenceSession CreateSession() => PersistenceSession.Create(ConnectionString);

    /// <summary>Empties every table, so each test starts from a clean database.</summary>
    public async Task ResetAsync()
    {
        await using VehicleRentalDbContext context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Rentals\", \"Customers\", \"Vehicles\", \"Users\" CASCADE");
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}

/// <summary>
/// Base class for database tests: they share one database and run one at a time, each starting empty.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class DatabaseTestBase : IAsyncLifetime
{
    protected DatabaseTestBase(PostgresFixture database) => Database = database;

    protected PostgresFixture Database { get; }

    public Task InitializeAsync() => TestDatabase.IsConfigured ? Database.ResetAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;
}

