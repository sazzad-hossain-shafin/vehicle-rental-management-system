using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VehicleRental.Application.Tenancy;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.Tenancy;

/// <summary>
/// The tenant-scoped transaction of one scope. Ordering, enforced here and proven by tests:
/// 1. the tenant is already bound (otherwise this refuses to start);
/// 2. the transaction is created;
/// 3. the interceptor applies the transaction-local tenant setting and confirms it;
/// 4. only then does this return, so every query, command and unit-of-work operation runs after the setting;
/// 5. commit or roll back ends the transaction and PostgreSQL discards the setting with it.
/// If the setting was not applied (for example the interceptor is missing) the transaction is rolled back and this throws.
/// </summary>
internal sealed class TenantTransaction : ITenantTransaction
{
    private readonly VehicleRentalDbContext _db;
    private readonly TenantContext _tenant;
    private readonly TenancyOptions _options;
    private IDbContextTransaction? _transaction;

    public TenantTransaction(VehicleRentalDbContext db, TenantContext tenant, IOptions<TenancyOptions> options)
    {
        _db = db;
        _tenant = tenant;
        _options = options.Value;
    }

    public bool IsActive => _transaction is not null;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new TenancyDisabledException();
        }

        _ = _tenant.RequireCompanyId(); // 1. fail closed: no tenant, no transaction

        if (_transaction is not null || _db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open for this scope.");
        }

        // EF Core sends a single-statement SaveChanges without a transaction, which would run with no tenant setting.
        _db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;

        _transaction = await _db.Database.BeginTransactionAsync(cancellationToken); // 2 and 3

        if (!_tenant.IsApplied(_transaction.GetDbTransaction()))
        {
            await RollbackAsync(CancellationToken.None);

            throw new TenantRequiredException("The tenant setting was not applied to the transaction.");
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction transaction = _transaction ?? throw new InvalidOperationException("There is no open tenant transaction.");
        _transaction = null;

        await using (transaction)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = _transaction;
        _transaction = null;

        if (transaction is not null)
        {
            await using (transaction)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // A transaction that was neither committed nor rolled back ends in a rollback, never a commit.
        IDbContextTransaction? transaction = _transaction;
        _transaction = null;

        if (transaction is not null)
        {
            await transaction.DisposeAsync();
        }
    }
}

/// <summary>Runs work for one company in its own scope and tenant transaction.</summary>
internal sealed class TenantScopeRunner : ITenantScopeRunner
{
    private readonly IServiceScopeFactory _scopes;

    public TenantScopeRunner(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<T> RunAsync<T>(
        Guid companyId,
        Func<IServiceProvider, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(companyId, TenantSource.Background);

        ITenantTransaction transaction = scope.ServiceProvider.GetRequiredService<ITenantTransaction>();
        await transaction.BeginAsync(cancellationToken);

        try
        {
            T result = await work(scope.ServiceProvider, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ForEachActiveCompanyAsync(
        Func<Guid, IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> ids;

        await using (AsyncServiceScope listing = _scopes.CreateAsyncScope())
        {
            ids = await listing.ServiceProvider.GetRequiredService<ICompanyDirectory>().ListActiveIdsAsync(cancellationToken);
        }

        foreach (Guid id in ids)
        {
            await RunAsync<object?>(
                id,
                async (services, token) =>
                {
                    await work(id, services, token);
                    return null;
                },
                cancellationToken);
        }
    }
}
