using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Infrastructure.Persistence.Configurations;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// Saves the unit of work with <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core wraps all the changes of one save in a single database transaction, so a rental,
/// its new customer and the vehicle's new status are saved together or not at all. A save that fails because
/// another request got there first is reported as a <see cref="ConflictException"/>. After any failed save the
/// context is in an undefined state and must be discarded.
/// </para>
/// <para>
/// <see cref="LockVehicleAsync"/> starts an explicit transaction and takes a PostgreSQL row lock
/// (<c>SELECT ... FOR UPDATE</c>) on the vehicle. The lock lives in the database, not in this process, so it
/// works the same whether the competing requests reach the same API instance or different ones. The
/// transaction is committed by <see cref="SaveChangesAsync"/> and rolled back (releasing the lock) if the save
/// fails or the unit of work is disposed without saving.
/// </para>
/// </remarks>
public sealed class UnitOfWork : IUnitOfWork, IAsyncDisposable
{
    private const string UniqueViolation = "23505";
    private const string ExclusionViolation = "23P01";
    private const string DeadlockDetected = "40P01";
    private const string SerializationFailure = "40001";
    private const string LockNotAvailable = "55P03";

    /// <summary>How long a request waits for another request's vehicle lock before reporting a conflict.</summary>
    private const string LockTimeout = "5s";

    private readonly VehicleRentalDbContext _db;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(VehicleRentalDbContext db) => _db = db;

    public async Task LockVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_transaction is null)
            {
                _transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

                // Waiting forever behind a stuck request is worse than a clear "try again": give up after a few
                // seconds. SET LOCAL lasts only for this transaction.
                await _db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '" + LockTimeout + "'", cancellationToken);
            }

            await _db.Database.ExecuteSqlAsync(
                $"SELECT \"Id\" FROM \"Vehicles\" WHERE \"Id\" = {vehicleId} FOR UPDATE",
                cancellationToken);
        }
        catch (Exception ex) when (FindPostgresError(ex) is { } error && ToConflict(error) is not null)
        {
            await DiscardTransactionAsync();

            throw ToConflict(error)!;
        }
        catch
        {
            await DiscardTransactionAsync();

            throw;
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);

            if (_transaction is not null)
            {
                await _transaction.CommitAsync(cancellationToken);
                await DiscardTransactionAsync();
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            await DiscardTransactionAsync();

            // A vehicle or rental row was changed by someone else after this request loaded it.
            throw new ConflictException(
                "The data was changed by another request. Please try again.");
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException
                                   && FindPostgresError(ex) is { } error
                                   && ToConflict(error) is not null)
        {
            await DiscardTransactionAsync();

            // Only the SQL state and constraint name are used; nothing from the database error reaches the client.
            throw ToConflict(error)!;
        }
        catch
        {
            await DiscardTransactionAsync();

            throw;
        }
    }

    public ValueTask DisposeAsync() => new(DiscardTransactionAsync());

    /// <summary>Rolls back and forgets the explicit transaction, if there is one. Rolling back releases the lock.</summary>
    private async Task DiscardTransactionAsync()
    {
        if (_transaction is { } transaction)
        {
            _transaction = null;

            try
            {
                await transaction.DisposeAsync();   // rolls back anything not yet committed
            }
            catch (Exception)
            {
                // The connection is already broken; there is nothing left to release.
            }
        }
    }

    /// <summary>
    /// EF Core may wrap a database error, for example in a "likely a transient failure" exception for
    /// deadlocks, so the error is looked for down the chain of inner exceptions.
    /// </summary>
    private static PostgresException? FindPostgresError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private static ConflictException? ToConflict(PostgresException error) => error.SqlState switch
    {
        UniqueViolation => new ConflictException(DescribeUniqueViolation(error.ConstraintName)),
        ExclusionViolation => new ConflictException(DescribeExclusionViolation(error.ConstraintName)),

        // Several requests competing for the same rows can make PostgreSQL abort the losers to break a
        // deadlock, or ask them to retry. The loser simply lost the race, like any other conflict.
        DeadlockDetected or SerializationFailure =>
            new ConflictException("The data was changed by another request. Please try again."),

        LockNotAvailable =>
            new ConflictException("The vehicle is being booked by another request. Please try again."),

        _ => null
    };

    private static string DescribeExclusionViolation(string? constraintName) => constraintName switch
    {
        ReservationConfiguration.NoOverlapConstraint => "The vehicle is already reserved for part of that period.",
        _ => "The change conflicts with existing data."
    };

    private static string DescribeUniqueViolation(string? constraintName) => constraintName switch
    {
        RentalConfiguration.ActiveRentalPerVehicleIndex => "The vehicle is already rented.",
        ReservationConfiguration.RentalIndex => "The reservation has already been picked up.",
        "IX_Vehicles_RegistrationNumber" => "A vehicle with that registration number already exists.",
        "IX_Customers_CustomerNumber" => "A customer with that customer number already exists.",
        _ => "The change conflicts with existing data."
    };
}
