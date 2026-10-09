using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace VehicleRental.TenantIsolation.Spike.Tenancy;

/// <summary>Raised when tenant-scoped code tries to talk to the database outside a tenant transaction.</summary>
public sealed class TenantScopeException : InvalidOperationException
{
    public TenantScopeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Applies the tenant to the database. Whenever ANY transaction starts on a tenant-bound context (the explicit one
/// from <see cref="TenantSession"/>, or the implicit one EF opens around SaveChanges), the first thing sent inside it
/// is <c>set_config('app.company_id', ..., true)</c>. The third argument <c>true</c> makes the setting
/// transaction-local: PostgreSQL discards it at COMMIT or ROLLBACK, whichever path the connection takes back to the
/// pool. There is nothing to reset and nothing that can be forgotten.
/// </summary>
public sealed class TenantTransactionInterceptor : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is TenantDbContext { CompanyId: { } companyId })
        {
            await using NpgsqlCommand command = new("SELECT set_config('app.company_id', @company, true)", (NpgsqlConnection)connection, (NpgsqlTransaction)result);
            command.Parameters.AddWithValue("company", companyId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        if (eventData.Context is TenantDbContext { CompanyId: { } companyId })
        {
            using NpgsqlCommand command = new("SELECT set_config('app.company_id', @company, true)", (NpgsqlConnection)connection, (NpgsqlTransaction)result);
            command.Parameters.AddWithValue("company", companyId.ToString());
            command.ExecuteNonQuery();
        }

        return result;
    }
}

/// <summary>
/// A guard against developer mistakes, not a substitute for Row-Level Security: it refuses to run a command for a
/// tenant-bound context outside a transaction (where the tenant setting would not exist and RLS would silently return
/// nothing), and refuses raw SQL that mentions the tenant setting (so a handler cannot switch tenant mid-request).
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
        if (eventData.Context is not TenantDbContext context)
        {
            return;
        }

        if (context.CompanyId is null)
        {
            throw new TenantScopeException("No company is set for this context, so it may not run any command.");
        }

        if (command.Transaction is null)
        {
            throw new TenantScopeException("Tenant data may only be used inside a tenant transaction.");
        }

        foreach (string word in Forbidden)
        {
            if (command.CommandText.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                throw new TenantScopeException($"SQL that touches the tenant setting ('{word}') is not allowed in tenant-scoped code.");
            }
        }
    }
}
