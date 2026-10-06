namespace VehicleRental.Application.Abstractions;

/// <summary>
/// Commits the changes a use case made through the repositories. Services call
/// it once, after the domain operations have succeeded.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
