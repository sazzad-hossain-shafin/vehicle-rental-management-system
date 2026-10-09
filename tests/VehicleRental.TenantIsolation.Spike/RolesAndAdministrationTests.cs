using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.TenantIsolation.Spike.Support;
using VehicleRental.TenantIsolation.Spike.Tenancy;

namespace VehicleRental.TenantIsolation.Spike;

/// <summary>Which database role the application uses decides whether RLS protects anything; plus the controlled escape hatch.</summary>
public sealed class RolesAndAdministrationTests : SpikeTestBase
{
    private static readonly string[] TenantTables = ["vehicles", "reservations", "legacy_vehicles", "naive_reservations"];

    public RolesAndAdministrationTests(SpikeDatabase database)
        : base(database)
    {
    }

    [SpikeFact]
    public async Task The_application_role_passes_the_safety_check()
    {
        await using NpgsqlConnection connection = await OpenAsync(AppPool(1));

        Assert.Empty(await RlsSafetyCheck.FindViolationsAsync(connection, TenantTables));
    }

    [SpikeFact]
    public async Task A_BYPASSRLS_role_is_detected_and_really_does_see_every_company()
    {
        await SeedAsync(3);
        await SeedAsync(4);

        // BYPASSRLS skips the policies, not the privileges: give this deliberately unsafe role ordinary table access.
        await using (NpgsqlConnection owner = await OpenAsync(Database.OwnerConnectionString()))
        {
            await ExecuteAsync(owner, $"GRANT USAGE ON SCHEMA public TO \"{Database.BypassRole}\"");
            await ExecuteAsync(owner, $"GRANT SELECT ON vehicles TO \"{Database.BypassRole}\"");
        }

        await using NpgsqlConnection connection = await OpenAsync(Database.BypassConnectionString());
        IReadOnlyList<string> violations = await RlsSafetyCheck.FindViolationsAsync(connection, TenantTables);

        Assert.Contains(violations, v => v.Contains("BYPASSRLS", StringComparison.Ordinal));
        Assert.True(await ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles") >= 7); // the check is not theoretical
    }

    [SpikeFact]
    public async Task A_superuser_is_detected()
    {
        await using NpgsqlConnection connection = await OpenAsync(Database.AdminConnectionString());

        IReadOnlyList<string> violations = await RlsSafetyCheck.FindViolationsAsync(connection, TenantTables);

        Assert.Contains(violations, v => v.Contains("superuser", StringComparison.Ordinal));
    }

    [SpikeFact]
    public async Task The_table_owner_is_still_filtered_because_RLS_is_forced_and_an_unforced_table_is_detected()
    {
        await SeedAsync(3);
        await using NpgsqlConnection owner = await OpenAsync(Database.OwnerConnectionString());

        // FORCE ROW LEVEL SECURITY: with no tenant even the owner sees nothing.
        Assert.Equal(0L, await ScalarAsync<long>(owner, "SELECT count(*) FROM vehicles"));
        Assert.Empty(await RlsSafetyCheck.FindViolationsAsync(owner, TenantTables));

        // A table the owner forgot to FORCE is exactly what the check must flag.
        await ExecuteAsync(owner, "CREATE TABLE unforced_probe (id int, company_id uuid)");
        await ExecuteAsync(owner, "ALTER TABLE unforced_probe ENABLE ROW LEVEL SECURITY");
        IReadOnlyList<string> violations = await RlsSafetyCheck.FindViolationsAsync(owner, ["unforced_probe"]);
        await ExecuteAsync(owner, "DROP TABLE unforced_probe");

        Assert.Contains(violations, v => v.Contains("not forced", StringComparison.Ordinal));
    }

    [SpikeFact]
    public async Task The_application_role_cannot_weaken_or_step_around_the_policies()
    {
        await using NpgsqlConnection connection = await OpenAsync(AppPool(1));

        foreach (string sql in new[]
                 {
                     "ALTER TABLE vehicles DISABLE ROW LEVEL SECURITY",
                     "ALTER TABLE vehicles NO FORCE ROW LEVEL SECURITY",
                     "DROP POLICY tenant_isolation ON vehicles",
                     "CREATE POLICY open ON vehicles USING (true)",
                     "CREATE TABLE sneaky (id int)",
                 })
        {
            PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
            Assert.Equal("42501", error.SqlState); // insufficient_privilege (not owner / no CREATE)
        }

        // Turning row_security off does not bypass anything for a non-owner: the query errors instead of returning rows.
        await ExecuteAsync(connection, "SET row_security = off");
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles"));
        Assert.Equal("42501", refused.SqlState);
        await ExecuteAsync(connection, "RESET row_security");
    }

    [SpikeFact]
    public async Task The_application_role_cannot_become_another_role()
    {
        await using NpgsqlConnection connection = await OpenAsync(AppPool(1));

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"SET ROLE \"{Database.OwnerRole}\""));

        Assert.Equal("42501", error.SqlState);
    }

    [SpikeFact]
    public async Task KNOWN_LIMITATION_code_with_arbitrary_SQL_can_still_name_a_different_company()
    {
        // RLS keyed on a setting protects against BUGS (a forgotten filter, a wrong query), not against an attacker who can
        // already run arbitrary SQL as the application role: that role can set the same setting. Compensating controls are
        // parameterised EF queries only, the command guard (see ConcurrencyAndLifecycleTests), and code review of any raw SQL.
        var (a, _) = await SeedAsync(1);
        var (b, _) = await SeedAsync(5);

        long seen = await AsTenantAsync<long>(AppPool(2), a, async (c, _) =>
        {
            await ExecuteAsync(c, "SELECT set_config('app.company_id', @b, true)", ("b", b.ToString()));

            return await ScalarAsync<long>(c, "SELECT count(*) FROM vehicles");
        });

        Assert.Equal(5L, seen);
    }

    [SpikeFact]
    public async Task The_platform_function_lists_every_company_but_only_its_slug_and_status()
    {
        var (a, _) = await SeedAsync(2);
        var (b, _) = await SeedAsync(2);
        await using NpgsqlConnection connection = await OpenAsync(AppPool(1));

        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand("SELECT id, slug, status FROM platform_list_companies()", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.Equal(3, reader.FieldCount); // no name, no internal note, nothing else
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        Assert.Contains(a, ids);
        Assert.Contains(b, ids);
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles")); // it opens no door to tenant data
    }

    [SpikeFact]
    public async Task The_platform_function_cannot_be_hijacked_with_a_temporary_table()
    {
        var (a, _) = await SeedAsync(1);
        await using NpgsqlConnection connection = await OpenAsync(AppPool(1));

        await ExecuteAsync(connection, "CREATE TEMP TABLE companies (id uuid, slug text, name text, status text, internal_note text)");
        await ExecuteAsync(connection, "INSERT INTO pg_temp.companies VALUES (gen_random_uuid(), 'fake', 'fake', 'Active', 'fake')");

        long fake = await ScalarAsync<long>(connection, "SELECT count(*) FROM platform_list_companies() WHERE slug = 'fake'");
        long real = await ScalarAsync<long>(connection, "SELECT count(*) FROM platform_list_companies() WHERE id = @id", ("id", a));

        Assert.Equal(0L, fake);
        Assert.Equal(1L, real);
    }

    [SpikeFact]
    public async Task A_background_job_visits_each_company_in_its_own_transaction_and_sees_only_that_company()
    {
        var (a, _) = await SeedAsync(2);
        var (b, _) = await SeedAsync(5);
        var session = new TenantSession(AppPool(maxPoolSize: 2));
        var seen = new Dictionary<Guid, long>();

        await session.ForEachCompanyAsync(async (company, db) =>
        {
            long count = await db.Vehicles.LongCountAsync();
            Assert.True(await db.Vehicles.AllAsync(v => v.CompanyId == company));
            lock (seen)
            {
                seen[company] = count;
            }
        });

        Assert.Equal(2L, seen[a]);
        Assert.Equal(5L, seen[b]);
    }

    [SpikeFact]
    public async Task A_company_sees_only_its_own_company_row()
    {
        var (a, _) = await SeedAsync(1);
        await SeedAsync(1);
        var session = new TenantSession(AppPool(maxPoolSize: 2));

        List<Guid> visible = await session.RunAsync(a, db => db.Companies.Select(c => c.Id).ToListAsync());

        Assert.Equal([a], visible);
    }
}
