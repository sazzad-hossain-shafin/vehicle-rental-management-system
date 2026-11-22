using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.Domain.Entities;
using VehicleRental.Infrastructure.IntegrationTests.Support;
using VehicleRental.Infrastructure.Persistence;
using VehicleRental.Infrastructure.Tenancy;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The restricted runtime role: what the provisioning SQL creates, what it can and cannot do, that the SQL is idempotent
/// and never elevates it, and that the startup safety check tells a safe role from an unsafe one. The SQL under test is the
/// very file Docker Compose's db-roles service runs.
/// </summary>
public class RuntimeRoleTests : IClassFixture<RuntimeRoleTests.NoPreviewDatabase>
{
    public sealed class NoPreviewDatabase : TenantTestDatabase
    {
        public NoPreviewDatabase() => PreviewRls = false;
    }

    private readonly NoPreviewDatabase _db;

    public RuntimeRoleTests(NoPreviewDatabase db) => _db = db;

    private async Task<NpgsqlConnection> OpenAppAsync()
    {
        var connection = new NpgsqlConnection(_db.AppPool(maxPoolSize: 2));
        await connection.OpenAsync();

        return connection;
    }

    private async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)value;
    }

    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    [DatabaseFact]
    public async Task The_role_has_only_the_attributes_it_should()
    {
        await using NpgsqlConnection owner = await OpenOwnerAsync();
        await using var command = new NpgsqlCommand(
            "SELECT rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls, rolcanlogin, rolinherit FROM pg_roles WHERE rolname = @u",
            owner);
        command.Parameters.AddWithValue("u", _db.AppUser);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.False(reader.GetBoolean(0), "superuser");
        Assert.False(reader.GetBoolean(1), "createdb");
        Assert.False(reader.GetBoolean(2), "createrole");
        Assert.False(reader.GetBoolean(3), "replication");
        Assert.False(reader.GetBoolean(4), "bypassrls");
        Assert.True(reader.GetBoolean(5), "login");
        Assert.False(reader.GetBoolean(6), "inherit");
    }

    [DatabaseFact]
    public async Task The_role_owns_nothing_and_is_a_member_of_no_role()
    {
        await using NpgsqlConnection owner = await OpenOwnerAsync();

        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                owner,
                $"SELECT count(*) FROM pg_class c JOIN pg_roles o ON o.oid = c.relowner WHERE o.rolname = '{_db.AppUser}' AND c.relnamespace NOT IN ('pg_catalog'::regnamespace, 'information_schema'::regnamespace)"));
        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                owner,
                $"SELECT count(*) FROM pg_auth_members m JOIN pg_roles u ON u.oid = m.member WHERE u.rolname = '{_db.AppUser}'"));
    }

    [DatabaseFact]
    public async Task The_role_can_do_what_the_api_does_on_every_application_table()
    {
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(_db.AppPool(2)));
        var customer = new Customer("RR-" + Guid.NewGuid().ToString("N")[..8], "Role Test");

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        customer = await context.Customers.SingleAsync(c => c.Id == customer.Id);
        context.Customers.Remove(customer);
        await context.SaveChangesAsync();

        // The migration history is readable (the API reports pending migrations) ...
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.True(await context.Database.CanConnectAsync());
    }

    [DatabaseFact]
    public async Task The_role_cannot_change_the_schema_the_migration_history_or_truncate()
    {
        await using NpgsqlConnection app = await OpenAppAsync();

        foreach (string sql in new[]
                 {
                     "CREATE TABLE sneaky (id int)",
                     "CREATE SCHEMA sneaky",
                     "ALTER TABLE \"Vehicles\" ADD COLUMN sneaky int",
                     "DROP TABLE \"Vehicles\"",
                     "TRUNCATE TABLE \"Customers\" CASCADE",
                     "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('x', 'y')",
                     "DELETE FROM \"__EFMigrationsHistory\"",
                     "UPDATE \"__EFMigrationsHistory\" SET \"ProductVersion\" = 'z'",
                     "CREATE ROLE sneaky LOGIN",
                     "CREATE DATABASE sneaky",
                     "ALTER ROLE CURRENT_USER SUPERUSER",
                     "CREATE EXTENSION IF NOT EXISTS pgcrypto",
                 })
        {
            await using var command = new NpgsqlCommand(sql, app);
            PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.True(error.SqlState is "42501" or "55000", $"'{sql}' was not refused with a privilege error (got {error.SqlState})");
        }
    }

    [DatabaseFact]
    public async Task The_role_cannot_become_the_owner_or_change_the_session_into_something_else()
    {
        await using NpgsqlConnection app = await OpenAppAsync();
        // The owner's real name: it differs between machines (CI's superuser is not called "postgres"), and a role that does not exist would fail for a different reason.
        string owner = new NpgsqlConnectionStringBuilder(_db.OwnerConnectionString).Username!;
        await using var command = new NpgsqlCommand($"SET ROLE \"{owner}\"", app);

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42501", error.SqlState);
    }

    [DatabaseFact]
    public async Task Provisioning_again_changes_nothing_and_the_role_keeps_working()
    {
        await _db.ProvisionRuntimeRoleAsync(_db.AppUser, "a-different-password-that-is-long-enough-1");
        await _db.ProvisionRuntimeRoleAsync(_db.AppUser, "a-different-password-that-is-long-enough-1");

        // The password was re-set by the second run, so the old connection string no longer works and a fresh role check passes.
        await Assert.ThrowsAsync<PostgresException>(async () => await OpenAppAsync());

        // Put the original password back so the other tests in this class still connect.
        await using NpgsqlConnection owner = await OpenOwnerAsync();
        await TenantTestDatabase.Execute(owner, $"ALTER ROLE \"{_db.AppUser}\" PASSWORD '{GetPassword()}'");
        await using NpgsqlConnection app = await OpenAppAsync();
        Assert.Equal(1L, await ScalarAsync<long>(app, "SELECT 1::bigint"));
    }

    // The fixture keeps the password private; the connection string carries it.
    private string GetPassword() => new NpgsqlConnectionStringBuilder(_db.AppConnectionString()).Password!;

    [DatabaseFact]
    public async Task Provisioning_brings_a_role_that_was_made_more_powerful_back_down()
    {
        await using (NpgsqlConnection owner = await OpenOwnerAsync())
        {
            await TenantTestDatabase.Execute(owner, $"ALTER ROLE \"{_db.AppUser}\" CREATEDB CREATEROLE REPLICATION BYPASSRLS INHERIT");
            await TenantTestDatabase.Execute(owner, "CREATE ROLE vr_it_helper_" + _db.AppUser[^12..] + " NOLOGIN");
            await TenantTestDatabase.Execute(owner, $"GRANT vr_it_helper_{_db.AppUser[^12..]} TO \"{_db.AppUser}\"");
            await TenantTestDatabase.Execute(owner, $"GRANT CREATE ON SCHEMA public TO \"{_db.AppUser}\"");
            await TenantTestDatabase.Execute(owner, $"GRANT INSERT ON \"__EFMigrationsHistory\" TO \"{_db.AppUser}\"");
        }

        await _db.ProvisionRuntimeRoleAsync(_db.AppUser, GetPassword());

        await using (NpgsqlConnection owner = await OpenOwnerAsync())
        {
            await using var command = new NpgsqlCommand(
                "SELECT rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolinherit FROM pg_roles WHERE rolname = @u",
                owner);
            command.Parameters.AddWithValue("u", _db.AppUser);
            Assert.False((bool)(await command.ExecuteScalarAsync())!);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(owner, $"SELECT count(*) FROM pg_auth_members m JOIN pg_roles u ON u.oid = m.member WHERE u.rolname = '{_db.AppUser}'"));
            await TenantTestDatabase.Execute(owner, "DROP ROLE vr_it_helper_" + _db.AppUser[^12..]);
        }

        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(_db.AppPool(2)));
        Assert.Empty(await new DatabaseRoleSafetyCheck(context).FindViolationsAsync());
    }

    [DatabaseFact]
    public async Task Tables_created_by_a_later_migration_are_usable_by_the_role_without_rerunning_anything()
    {
        await using (NpgsqlConnection owner = await OpenOwnerAsync())
        {
            await TenantTestDatabase.Execute(owner, "CREATE TABLE \"LaterTable\" (\"Id\" serial PRIMARY KEY, \"Name\" text)");
        }

        await using NpgsqlConnection app = await OpenAppAsync();
        await TenantTestDatabase.Execute(app, "INSERT INTO \"LaterTable\" (\"Name\") VALUES ('x')"); // also uses the sequence
        Assert.Equal(1L, await ScalarAsync<long>(app, "SELECT count(*) FROM \"LaterTable\""));

        PostgresException truncate = await Assert.ThrowsAsync<PostgresException>(() => TenantTestDatabase.Execute(app, "TRUNCATE \"LaterTable\""));
        Assert.Equal("42501", truncate.SqlState);

        await using NpgsqlConnection cleanup = await OpenOwnerAsync();
        await TenantTestDatabase.Execute(cleanup, "DROP TABLE \"LaterTable\"");
    }

    [DatabaseTheory]
    [InlineData("", "a-password-that-is-long-enough")]
    [InlineData("Bad-Name", "a-password-that-is-long-enough")]
    [InlineData("ab", "a-password-that-is-long-enough")]
    [InlineData("valid_role_name", "s3cr3t-x")]
    [InlineData("postgres", "a-password-that-is-long-enough")]
    public async Task Provisioning_refuses_a_bad_role_name_a_short_password_or_a_privileged_name(string user, string password)
    {
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => _db.ProvisionRuntimeRoleAsync(user, password));

        Assert.Equal("P0001", error.SqlState);
        Assert.DoesNotContain(password, error.MessageText + error.Detail, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Provisioning_refuses_when_the_runtime_role_would_be_the_migration_owner()
    {
        string owner = new NpgsqlConnectionStringBuilder(_db.OwnerConnectionString).Username!;

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => _db.ProvisionRuntimeRoleAsync(owner, "a-password-that-is-long-enough"));

        Assert.Contains("different role", error.MessageText, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Provisioning_stops_if_the_runtime_role_somehow_owns_an_object()
    {
        string name = "vr_it_odd_" + Guid.NewGuid().ToString("N")[..10];
        await using (NpgsqlConnection owner = await OpenOwnerAsync())
        {
            await TenantTestDatabase.Execute(owner, $"CREATE ROLE \"{name}\" LOGIN");
            await TenantTestDatabase.Execute(owner, $"CREATE TABLE \"OddlyOwned\" (id int)");
            await TenantTestDatabase.Execute(owner, $"ALTER TABLE \"OddlyOwned\" OWNER TO \"{name}\"");
        }

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => _db.ProvisionRuntimeRoleAsync(name, "a-password-that-is-long-enough"));
        Assert.Contains("must own nothing", error.MessageText, StringComparison.Ordinal);

        await using NpgsqlConnection cleanup = await OpenOwnerAsync();
        await TenantTestDatabase.Execute(cleanup, "DROP TABLE \"OddlyOwned\"");
        await TenantTestDatabase.Execute(cleanup, $"DROP ROLE \"{name}\"");
    }

    // ---- the startup safety check ------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task The_safety_check_passes_for_the_runtime_role()
    {
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(_db.AppPool(2)));

        Assert.Empty(await new DatabaseRoleSafetyCheck(context).FindViolationsAsync());
    }

    [DatabaseFact]
    public async Task The_safety_check_rejects_a_superuser_and_the_table_owner()
    {
        // The test server's admin role is a superuser that also owns the tables: every kind of unsafe at once.
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(_db.OwnerConnectionString));

        IReadOnlyList<string> violations = await new DatabaseRoleSafetyCheck(context).FindViolationsAsync();

        Assert.Contains(violations, v => v.Contains("superuser", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("owns", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("migration history", StringComparison.Ordinal));
    }

    [DatabaseFact]
    public async Task The_safety_check_notices_each_single_privilege_and_never_names_the_role_or_the_password()
    {
        var cases = new (string Grant, string Expect)[]
        {
            ("ALTER ROLE \"{0}\" BYPASSRLS", "BYPASSRLS"),
            ("ALTER ROLE \"{0}\" CREATEROLE", "create roles"),
            ("ALTER ROLE \"{0}\" CREATEDB", "create databases"),
            ("ALTER ROLE \"{0}\" REPLICATION", "replication"),
            ("GRANT CREATE ON SCHEMA public TO \"{0}\"", "create objects in the application schema"),
            ("GRANT INSERT ON \"__EFMigrationsHistory\" TO \"{0}\"", "migration history"),
        };

        foreach ((string grant, string expect) in cases)
        {
            await using (NpgsqlConnection owner = await OpenOwnerAsync())
            {
                await TenantTestDatabase.Execute(owner, string.Format(grant, _db.AppUser));
            }

            await using (var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(_db.AppPool(2))))
            {
                IReadOnlyList<string> violations = await new DatabaseRoleSafetyCheck(context).FindViolationsAsync();

                Assert.Contains(violations, v => v.Contains(expect, StringComparison.OrdinalIgnoreCase));
                Assert.All(violations, v => Assert.DoesNotContain(_db.AppUser, v, StringComparison.Ordinal));
                Assert.All(violations, v => Assert.DoesNotContain(GetPassword(), v, StringComparison.Ordinal));
            }

            await _db.ProvisionRuntimeRoleAsync(_db.AppUser, GetPassword()); // put the role back
        }
    }

    [DatabaseFact]
    public async Task The_safety_check_notices_ownership_through_membership_of_an_owner_role()
    {
        string ownerRole = new NpgsqlConnectionStringBuilder(_db.OwnerConnectionString).Username!;

        await using (NpgsqlConnection owner = await OpenOwnerAsync())
        {
            await TenantTestDatabase.Execute(owner, $"GRANT \"{ownerRole}\" TO \"{_db.AppUser}\"");
            await TenantTestDatabase.Execute(owner, $"ALTER ROLE \"{_db.AppUser}\" INHERIT");
        }

        await using (var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(_db.AppPool(2))))
        {
            Assert.Contains(await new DatabaseRoleSafetyCheck(context).FindViolationsAsync(), v => v.Contains("owns", StringComparison.Ordinal));
        }

        await _db.ProvisionRuntimeRoleAsync(_db.AppUser, GetPassword()); // revokes the membership again
    }
}
