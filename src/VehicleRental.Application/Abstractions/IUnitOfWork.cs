namespace VehicleRental.Application.Abstractions;

/// <summary>
/// Commits the changes a use case made through the repositories, as one atomic unit.
/// Services call it once, after the domain operations have succeeded.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves every pending change, or none of them.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">
    /// The changes clash with another request: a duplicate identifier, a vehicle that
    /// was rented or changed by someone else in the meantime, and so on. Nothing was saved,
    /// and the repositories used for this unit of work should be discarded.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes an exclusive database lock on the vehicle, so that every operation that creates a claim on it (a
    /// rental or a reservation) runs one after another, even when the requests arrive at different API instances.
    /// Call it <b>before</b> checking whether the vehicle is free: once the lock is held, the check sees everything
    /// the previous holder committed. The lock is held until <see cref="SaveChangesAsync"/> succeeds or the unit of
    /// work is discarded, whichever comes first, and is released on failure too.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">The vehicle stayed locked by another request for too long.</exception>
    Task LockVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);
}
