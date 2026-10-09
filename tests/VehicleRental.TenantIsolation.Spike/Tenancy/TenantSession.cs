using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace VehicleRental.TenantIsolation.Spike.Tenancy;

/// <summary>
/// The only way tenant data is reached in the proof of concept: one transaction per unit of work, bound to one
/// company. Nothing is left on the connection when it ends.
/// </summary>
public sealed class TenantSession
{
    private readonly string _connectionString;
    private readonly TenantTransactionInterceptor _transactions = new();
    private readonly TenantCommandGuard _guard = new();

    public TenantSession(string connectionString) => _connectionString = connectionString;

    public TenantDbContext CreateContext(Guid? companyId) =>
        new(
            new DbContextOptionsBuilder<TenantDbContext>()
                .UseNpgsql(_connectionString)
                .AddInterceptors(_transactions, _guard)
                .Options,
            companyId);

    /// <summary>Runs <paramref name="work"/> for one company inside one transaction; commits on success, rolls back otherwise.</summary>
    public async Task<T> RunAsync<T>(
        Guid companyId,
        Func<TenantDbContext, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        await using TenantDbContext db = CreateContext(companyId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        T result = await work(db);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    public Task RunAsync(Guid companyId, Func<TenantDbContext, Task> work, CancellationToken cancellationToken = default) =>
        RunAsync<object?>(companyId, async db =>
        {
            await work(db);
            return null;
        }, cancellationToken);

    /// <summary>
    /// An administrative or background operation: it is never "all companies at once". It lists company IDs through the
    /// narrow platform function, then does the real work once per company, each in its own tenant transaction.
    /// </summary>
    public async Task ForEachCompanyAsync(Func<Guid, TenantDbContext, Task> work, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [];

        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT id FROM platform_list_companies()", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        foreach (Guid id in ids)
        {
            await RunAsync(id, db => work(id, db), cancellationToken);
        }
    }
}
