using Microsoft.EntityFrameworkCore;
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
/// Reports whether the database is reachable and its schema is up to date, without changing anything.
/// Used by the health check, the startup diagnostics and the console client. It never exposes
/// connection details.
/// </summary>
public sealed class DatabaseStatusChecker
{
    private readonly VehicleRentalDbContext _db;

    public DatabaseStatusChecker(VehicleRentalDbContext db) => _db = db;

    public async Task<DatabaseStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!await _db.Database.CanConnectAsync(cancellationToken))
        {
            return DatabaseStatus.CannotConnect;
        }

        var pending = await _db.Database.GetPendingMigrationsAsync(cancellationToken);

        return pending.Any() ? DatabaseStatus.MigrationsPending : DatabaseStatus.Ready;
    }
}
