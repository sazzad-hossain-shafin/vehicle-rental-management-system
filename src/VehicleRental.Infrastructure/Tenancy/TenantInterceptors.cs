using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using VehicleRental.Application.Tenancy;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.Tenancy;

/// <summary>
/// Applies the tenant to the database, inside the transaction, before anything else can run in it.
///
/// The order is fixed: the tenant is bound, THEN a transaction starts, and the first statement inside that transaction
/// is <c>set_config('app.company_id', ..., true)</c>. The third argument <c>true</c> makes the setting transaction-local:
/// PostgreSQL discards it at COMMIT or ROLLBACK on every path, so it cannot reach a pooled connection's next user and
/// nothing has to be reset. A session-scoped setting is never used. A context with no tenant is not touched at all.
/// </summary>
public sealed class TenantTransactionInterceptor : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (TenantOf(eventData) is { IsBound: true } tenant)
        {
            Guid company = tenant.RequireCompanyId();

            await using NpgsqlCommand command = Command(connection, result, company);
            Verify(await command.ExecuteScalarAsync(cancellationToken), company);
            tenant.MarkApplied(result);
        }

        return result;
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        if (TenantOf(eventData) is { IsBound: true } tenant)
        {
            Guid company = tenant.RequireCompanyId();

            using NpgsqlCommand command = Command(connection, result, company);
            Verify(command.ExecuteScalar(), company);
            tenant.MarkApplied(result);
        }

        return result;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        TenantOf(eventData)?.ClearApplied(transaction);

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        TenantOf(eventData)?.ClearApplied(transaction);

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        TenantOf(eventData)?.ClearApplied(transaction);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TenantOf(eventData)?.ClearApplied(transaction);
        return Task.CompletedTask;
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TenantOf(eventData)?.ClearApplied(transaction);
        return Task.CompletedTask;
    }

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        TenantOf(eventData)?.ClearApplied(transaction);
        return Task.CompletedTask;
    }

    private static TenantContext? TenantOf(TransactionEndEventData eventData) => (eventData.Context as VehicleRentalDbContext)?.Tenant;

    private static TenantContext? TenantOf(TransactionErrorEventData eventData) => (eventData.Context as VehicleRentalDbContext)?.Tenant;

    private static NpgsqlCommand Command(DbConnection connection, DbTransaction transaction, Guid company)
    {
        // set_config returns the value it set, so the same round trip proves what the transaction now carries.
        var command = new NpgsqlCommand("SELECT set_config('app.company_id', @company, true)", (NpgsqlConnection)connection, (NpgsqlTransaction)transaction);
        command.Parameters.AddWithValue("company", company.ToString());

        return command;
    }

    private static void Verify(object? returned, Guid company)
    {
        if (returned is not string text || !Guid.TryParse(text, out Guid applied) || applied != company)
        {
            throw new TenantRequiredException("The tenant setting could not be confirmed in the transaction.");
        }
    }
}

/// <summary>
/// A guard against mistakes, not the security boundary (Row-Level Security is). For a context with a bound tenant it
/// refuses every command that does not run inside the transaction the tenant setting was applied to (so a read can
/// never run with no setting and silently return nothing, or with a stale one), and refuses raw SQL that touches the
/// tenant setting or the role. A context with NO tenant is not affected in any way: that is every existing code path.
/// </summary>
public sealed class TenantCommandGuard : DbCommandInterceptor
{
    private static readonly string[] Forbidden = ["app.company_id", "set_config", "set role", "set session", "row_security"];

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Check(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Check(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Check(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    private static void Check(DbCommand command, CommandEventData eventData)
    {
        if ((eventData.Context as VehicleRentalDbContext)?.Tenant is not { IsBound: true } tenant)
        {
            return; // no tenant: behave exactly as before
        }

        if (!tenant.IsApplied(command.Transaction))
        {
            throw new TenantRequiredException("A tenant-scoped command must run inside the tenant transaction.");
        }

        foreach (string word in Forbidden)
        {
            if (command.CommandText.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                throw new TenantRequiredException("SQL that touches the tenant setting is not allowed in tenant-scoped code.");
            }
        }
    }
}
