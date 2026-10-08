using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Infrastructure.Persistence.Configurations;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// Saves the unit of work with <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
/// </summary>
/// <remarks>
/// EF Core wraps all the changes of one save in a single database transaction, so a rental,
/// its new customer and the vehicle's new status are saved together or not at all; no second
/// transaction mechanism is needed. A save that fails because another request got there first is
/// reported as a <see cref="ConflictException"/>. After any failed save the context is in an
/// undefined state and must be discarded.
/// </remarks>
public sealed class UnitOfWork : IUnitOfWork
{
    private const string UniqueViolation = "23505";
    private const string ExclusionViolation = "23P01";
    private const string DeadlockDetected = "40P01";
    private const string SerializationFailure = "40001";

    private readonly VehicleRentalDbContext _db;

    public UnitOfWork(VehicleRentalDbContext db) => _db = db;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A vehicle or rental row was changed by someone else after this request loaded it.
            throw new ConflictException(
                "The data was changed by another request. Please try again.");
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException
                                   && FindPostgresError(ex) is { } error
                                   && ToConflict(error) is not null)
        {
            // Only the SQL state and constraint name are used; nothing from the database error reaches the client.
            throw ToConflict(FindPostgresError(ex)!)!;
        }
    }

    /// <summary>
    /// EF Core may wrap a database error, for example in an "likely a transient failure" exception for
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
