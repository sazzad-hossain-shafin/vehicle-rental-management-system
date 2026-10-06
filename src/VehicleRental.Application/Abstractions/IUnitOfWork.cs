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
}
