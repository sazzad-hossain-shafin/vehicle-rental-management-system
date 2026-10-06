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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation } violation)
        {
            throw new ConflictException(DescribeUniqueViolation(violation.ConstraintName));
        }
    }

    private static string DescribeUniqueViolation(string? constraintName) => constraintName switch
    {
        RentalConfiguration.ActiveRentalPerVehicleIndex => "The vehicle is already rented.",
        "IX_Vehicles_RegistrationNumber" => "A vehicle with that registration number already exists.",
        "IX_Customers_CustomerNumber" => "A customer with that customer number already exists.",
        _ => "The change conflicts with existing data."
    };
}
