using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Abstractions;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure;

/// <summary>Whether the database is ready for the application to use.</summary>
public enum DatabaseStatus
{
    Ready,

    /// <summary>The server or the database could not be reached with the configured connection string.</summary>
    CannotConnect,

    /// <summary>The database is reachable but its schema is missing or out of date.</summary>
    MigrationsPending
}

/// <summary>
/// One database context with the repositories and unit of work that share it. A client that has
/// no dependency injection of its own (the console app) creates one per command and disposes it,
/// so a context never lives long enough to hold stale data.
/// </summary>
public sealed class PersistenceSession : IAsyncDisposable
{
    private readonly VehicleRentalDbContext _db;

    private PersistenceSession(VehicleRentalDbContext db)
    {
        _db = db;
        Vehicles = new VehicleRepository(db);
        Customers = new CustomerRepository(db);
        Rentals = new RentalRepository(db);
        UnitOfWork = new UnitOfWork(db);
    }

    /// <summary>The key under "ConnectionStrings" in configuration.</summary>
    public const string ConnectionStringName = VehicleRentalDbContextOptions.ConnectionStringName;

    /// <summary>The configuration key as an environment variable name.</summary>
    public const string ConnectionStringEnvironmentVariable = VehicleRentalDbContextOptions.ConnectionStringEnvironmentVariable;

    public IVehicleRepository Vehicles { get; }

    public ICustomerRepository Customers { get; }

    public IRentalRepository Rentals { get; }

    public IUnitOfWork UnitOfWork { get; }

    public static PersistenceSession Create(string connectionString) =>
        new(new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(connectionString)));

    /// <summary>
    /// Checks the connection and whether every migration has been applied. It never changes the database.
    /// </summary>
    public async Task<DatabaseStatus> CheckDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (!await _db.Database.CanConnectAsync(cancellationToken))
        {
            return DatabaseStatus.CannotConnect;
        }

        var pending = await _db.Database.GetPendingMigrationsAsync(cancellationToken);

        return pending.Any() ? DatabaseStatus.MigrationsPending : DatabaseStatus.Ready;
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
