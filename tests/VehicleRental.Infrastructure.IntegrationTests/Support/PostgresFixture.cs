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

    private string? _runtimeRole;

    /// <summary>What the tests use: the owner, or (with VEHICLERENTAL_TEST_RUNTIME_ROLE=1) the restricted runtime role.</summary>
    public string ConnectionString { get; private set; } = "";

    /// <summary>The owner: migrations and the cleanup between tests always use it.</summary>
    public string OwnerConnectionString { get; private set; } = "";

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

        OwnerConnectionString = WithDatabase(_serverConnectionString, _databaseName);

        await using (var owner = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(OwnerConnectionString)))
        {
            await owner.Database.MigrateAsync();
        }

        if (RuntimeRole.Enabled)
        {
            (_runtimeRole, string runtimeConnection) = await RuntimeRole.ProvisionAsync(OwnerConnectionString);
            ConnectionString = runtimeConnection;
        }
        else
        {
            ConnectionString = OwnerConnectionString;
        }
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

        if (_runtimeRole is not null)
        {
            await RuntimeRole.DropAsync(_serverConnectionString, _runtimeRole);
        }
    }

    public VehicleRentalDbContext CreateContext() =>
        new(VehicleRentalDbContextOptions.Create(ConnectionString));

    public PersistenceSession CreateSession() => PersistenceSession.Create(ConnectionString);

    /// <summary>Empties every table, so each test starts from a clean database.</summary>
    public async Task ResetAsync()
    {
        // The owner empties the tables: the runtime role has no TRUNCATE (nor should it).
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(OwnerConnectionString));
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Rentals\", \"Customers\", \"Vehicles\", \"Users\", \"Companies\" CASCADE");
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

