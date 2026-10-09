using Npgsql;

namespace VehicleRental.TenantIsolation.Spike.Tenancy;

/// <summary>
/// A startup check the real application should run: Row-Level Security protects nothing if the application connects
/// as a superuser, a role with BYPASSRLS, or the owner of an un-forced table. It returns what is wrong, not just yes/no.
/// </summary>
public static class RlsSafetyCheck
{
    public static async Task<IReadOnlyList<string>> FindViolationsAsync(
        NpgsqlConnection connection,
        IEnumerable<string> tenantTables,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();

        await using (var role = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user", connection))
        await using (var reader = await role.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetBoolean(0))
                {
                    violations.Add("the connection role is a superuser, which bypasses Row-Level Security");
                }

                if (reader.GetBoolean(1))
                {
                    violations.Add("the connection role has BYPASSRLS");
                }
            }
        }

        foreach (string table in tenantTables)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT c.relrowsecurity,
                       c.relforcerowsecurity,
                       pg_has_role(current_user, c.relowner, 'USAGE') AS is_owner
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname = @table
                """,
                connection);
            command.Parameters.AddWithValue("table", table);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                violations.Add($"table '{table}' does not exist");
                continue;
            }

            bool enabled = reader.GetBoolean(0);
            bool forced = reader.GetBoolean(1);
            bool owner = reader.GetBoolean(2);

            if (!enabled)
            {
                violations.Add($"Row-Level Security is not enabled on '{table}'");
            }

            if (owner && !forced)
            {
                violations.Add($"the connection role owns '{table}' and RLS is not forced, so the owner bypasses it");
            }
        }

        return violations;
    }
}
