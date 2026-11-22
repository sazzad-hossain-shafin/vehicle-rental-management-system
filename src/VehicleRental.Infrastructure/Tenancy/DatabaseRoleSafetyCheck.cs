using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.Tenancy;

/// <summary>
/// Checks that the database role the application is connected as is safe to run behind Row-Level Security: Row-Level
/// Security protects nothing if the application is a superuser, has BYPASSRLS, owns the tables, or can change the schema.
/// It reports what is wrong in plain words and never includes the role name, a password or a connection string.
///
/// The "RLS is enabled on the tenant tables" part of the spike's check is deliberately not here: no production table has
/// RLS yet (Phase 14A-7 activates it and adds that check).
/// </summary>
public sealed class DatabaseRoleSafetyCheck
{
    private readonly VehicleRentalDbContext _db;

    public DatabaseRoleSafetyCheck(VehicleRentalDbContext db) => _db = db;

    /// <summary>The problems found; empty means the role is safe.</summary>
    public async Task<IReadOnlyList<string>> FindViolationsAsync(CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        DbConnection connection = _db.Database.GetDbConnection();
        bool opened = connection.State != ConnectionState.Open;

        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using (DbCommand role = connection.CreateCommand())
            {
                role.CommandText = "SELECT rolsuper, rolbypassrls, rolcreaterole, rolcreatedb, rolreplication FROM pg_roles WHERE rolname = current_user";
                await using DbDataReader reader = await role.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    violations.Add("the connection role could not be inspected");
                }
                else
                {
                    Add(violations, reader.GetBoolean(0), "the application connects as a superuser, which bypasses Row-Level Security");
                    Add(violations, reader.GetBoolean(1), "the application's database role has BYPASSRLS");
                    Add(violations, reader.GetBoolean(2), "the application's database role can create roles");
                    Add(violations, reader.GetBoolean(3), "the application's database role can create databases");
                    Add(violations, reader.GetBoolean(4), "the application's database role has replication privileges");
                }
            }

            // Ownership, including through MEMBERSHIP of an owner role: a role with NOINHERIT can still SET ROLE into a role it
            // is a member of, so membership (not just inherited use) counts. An owner is not bound by the policies unless forced.
            long owned = await ScalarAsync<long>(
                connection,
                """
                SELECT count(*) FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
                  AND pg_has_role(current_user, c.relowner, 'MEMBER')
                """,
                cancellationToken);
            Add(violations, owned > 0, "the application's database role owns (or is a member of the owner of) tables in the application schema");

            Add(
                violations,
                await ScalarAsync<bool>(connection, "SELECT has_schema_privilege(current_user, 'public', 'CREATE')", cancellationToken),
                "the application's database role can create objects in the application schema");

            Add(
                violations,
                await ScalarAsync<bool>(connection, "SELECT has_database_privilege(current_user, current_database(), 'CREATE')", cancellationToken),
                "the application's database role can create schemas in the database");

            // Migration privileges: the migration history is the one table only the migration owner may change.
            Add(
                violations,
                await ScalarAsync<bool>(
                    connection,
                    """
                    SELECT coalesce(
                        has_table_privilege(current_user, to_regclass('public."__EFMigrationsHistory"'), 'INSERT')
                        OR has_table_privilege(current_user, to_regclass('public."__EFMigrationsHistory"'), 'UPDATE')
                        OR has_table_privilege(current_user, to_regclass('public."__EFMigrationsHistory"'), 'DELETE'), false)
                    """,
                    cancellationToken),
                "the application's database role can change the migration history");
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        return violations;
    }

    private static void Add(List<string> violations, bool condition, string message)
    {
        if (condition)
        {
            violations.Add(message);
        }
    }

    private static async Task<T> ScalarAsync<T>(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null or DBNull ? default! : (T)value;
    }
}
