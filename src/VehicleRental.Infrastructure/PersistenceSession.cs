using VehicleRental.Application.Abstractions;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure;

/// <summary>
/// One database context with the repositories and unit of work that share it, for a client that has
/// no dependency injection of its own (the console app). It creates one per command and disposes it,
/// so a context never lives long enough to hold stale data. The web API uses dependency injection
/// instead (see <see cref="DependencyInjection"/>).
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
        Reservations = new ReservationRepository(db);
        Availability = new VehicleAvailabilityQuery(db);
        UnitOfWork = new UnitOfWork(db);
    }

    /// <summary>The key under "ConnectionStrings" in configuration.</summary>
    public const string ConnectionStringName = VehicleRentalDbContextOptions.ConnectionStringName;

    /// <summary>The configuration key as an environment variable name.</summary>
    public const string ConnectionStringEnvironmentVariable = VehicleRentalDbContextOptions.ConnectionStringEnvironmentVariable;

    public IVehicleRepository Vehicles { get; }

    public ICustomerRepository Customers { get; }

    public IRentalRepository Rentals { get; }

    public IReservationRepository Reservations { get; }

    public IVehicleAvailabilityQuery Availability { get; }

    public IUnitOfWork UnitOfWork { get; }

    public static PersistenceSession Create(string connectionString) =>
        new(new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(connectionString)));

    /// <summary>
    /// Checks the connection and whether every migration has been applied. It never changes the database.
    /// </summary>
    public Task<DatabaseStatus> CheckDatabaseAsync(CancellationToken cancellationToken = default) =>
        new DatabaseStatusChecker(_db).CheckAsync(cancellationToken);

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
