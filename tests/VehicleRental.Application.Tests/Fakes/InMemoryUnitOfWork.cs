using VehicleRental.Application.Abstractions;

namespace VehicleRental.Application.Tests.Fakes;

/// <summary>
/// Test double. The fake repositories change their collections immediately, so there is
/// nothing to commit here.
/// </summary>
internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}
