using VehicleRental.Application.Abstractions;

namespace VehicleRental.Application.InMemory;

/// <summary>
/// TEMPORARY. The in-memory repositories change their collections immediately, so
/// there is nothing to commit. A database-backed implementation will save here.
/// </summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}
