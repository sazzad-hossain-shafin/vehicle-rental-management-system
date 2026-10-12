using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VehicleRental.Infrastructure.IntegrationTests.Support;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The AddCompanyTenancyExpand migration against disposable databases: it must not change a single existing row,
/// must roll back cleanly while the new structures are empty, must refuse to roll back once they hold data, and must
/// not wait behind a long transaction. Each test builds its own database at the migration before it, so none of this
/// touches a development database.
/// </summary>
public class TenancyExpandMigrationTests
{
    private const string Before = "AddReservations";
    private const string Expand = "AddCompanyTenancyExpand";

    private static readonly string[] Tables = ["Users", "Roles", "UserRoles", "Customers", "Vehicles", "Rentals", "Reservations"];

    // ---- a throwaway database ---------------------------------------------------------------------------------

    private sealed class ScratchDatabase : IAsyncDisposable
    {
        private readonly string _server;
        private readonly string _name;

        private ScratchDatabase(string server, string name)
        {
            _server = server;
            _name = name;
            ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
        }

        public string ConnectionString { get; }

        public static async Task<ScratchDatabase> CreateAsync()
        {
            string server = TestDatabase.ServerConnectionString!;
            string name = "vehiclerental_it_expand_" + Guid.NewGuid().ToString("N");

            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();

            return new ScratchDatabase(server, name);
        }

        public VehicleRentalDbContext CreateContext() => new(VehicleRentalDbContextOptions.Create(ConnectionString));

        public async Task MigrateAsync(string? target = null)
        {
            await using VehicleRentalDbContext context = CreateContext();
            await context.GetService<IMigrator>().MigrateAsync(target);
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);

            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<string>> QueryAsync(string sql)
        {
            var rows = new List<string>();
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString())));
            }

            return rows;
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Realistic rows in every existing table, written with plain SQL against the schema as it was before.</summary>
    private static async Task SeedPreviousSchemaAsync(ScratchDatabase db)
    {
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid();
        Guid car = Guid.NewGuid(), bike = Guid.NewGuid(), van = Guid.NewGuid();
        Guid activeRental = Guid.NewGuid(), doneRental = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO \"Customers\" (\"Id\", \"CustomerNumber\", \"Name\") VALUES (@a, 'WEB-AAA', 'Alice'), (@b, 'WEB-BBB', 'Bob')",
            ("a", alice),
            ("b", bob));

        await db.ExecuteAsync(
            """
            INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "CustomerId")
            VALUES (@id, 'alice@example.test', 'ALICE@EXAMPLE.TEST', 'alice@example.test', 'ALICE@EXAMPLE.TEST', false, 'not-a-real-hash', 'stamp', 'concurrency', false, false, true, 0, @c)
            """,
            ("id", Guid.NewGuid()),
            ("c", alice));

        await db.ExecuteAsync(
            """
            INSERT INTO "Vehicles" ("Id", "RegistrationNumber", "Make", "Model", "Year", "VehicleType", "DailyRate", "AvailabilityStatus") VALUES
            (@car, 'CAR-001', 'Toyota', 'Corolla', 2022, 'Car', 55.00, 'Rented'),
            (@bike, 'BIKE-01', 'Honda', 'CB500', 2023, 'Motorcycle', 38.00, 'Available'),
            (@van, 'VAN-001', 'Ford', 'Transit', 2021, 'Van', 90.00, 'Available')
            """,
            ("car", car),
            ("bike", bike),
            ("van", van));

        await db.ExecuteAsync(
            """
            INSERT INTO "Rentals" ("Id", "CustomerId", "VehicleId", "StartDate", "ExpectedReturnDate", "ActualReturnDate", "Status", "DailyRateAtRental", "BillableDays", "PricingDescription", "TotalCost") VALUES
            (@r1, @alice, @car, '2026-10-10', '2026-10-14', NULL, 'Active', 55.00, 4, 'Normal pricing', 220.00),
            (@r2, @bob, @bike, '2026-09-01', '2026-09-04', '2026-09-04', 'Completed', 38.00, 3, 'Normal pricing', 114.00)
            """,
            ("r1", activeRental),
            ("r2", doneRental),
            ("alice", alice),
            ("bob", bob),
            ("car", car),
            ("bike", bike));

        await db.ExecuteAsync(
            """
            INSERT INTO "Reservations" ("Id", "CustomerId", "VehicleId", "StartDate", "EndDate", "Status", "CreatedAt", "CancelledAt", "FulfilledAt", "RentalId", "DailyRateAtReservation", "BillableDays", "PricingDescription", "TotalCost") VALUES
            (@x1, @alice, @van, '2026-12-01', '2026-12-04', 'Active', now(), NULL, NULL, NULL, 90.00, 3, 'Normal pricing', 270.00),
            (@x2, @bob, @van, '2026-12-10', '2026-12-12', 'Cancelled', now(), now(), NULL, NULL, 90.00, 2, 'Normal pricing', 180.00),
            (@x3, @alice, @car, '2026-10-10', '2026-10-14', 'Fulfilled', now(), NULL, now(), @rental, 55.00, 4, 'Normal pricing', 220.00)
            """,
            ("x1", Guid.NewGuid()),
            ("x2", Guid.NewGuid()),
            ("x3", Guid.NewGuid()),
            ("alice", alice),
            ("bob", bob),
            ("van", van),
            ("car", car),
            ("rental", activeRental));
    }

    /// <summary>
    /// Per table: row count, and one hash over every row's OLD columns together with its row version (xmin). The new
    /// tenant columns are removed from the row before hashing, so the hash is comparable before and after the migration.
    /// </summary>
    private static async Task<Dictionary<string, string>> FingerprintAsync(ScratchDatabase db)
    {
        var result = new Dictionary<string, string>();

        foreach (string table in Tables)
        {
            List<string> rows = await db.QueryAsync(
                $"""
                SELECT count(*), coalesce(md5(string_agg(xmin::text || ':' || j, '|' ORDER BY j)), 'empty')
                FROM (SELECT xmin, (to_jsonb(t) - 'CompanyId' - 'CompanyCustomerId')::text AS j FROM "{table}" t) s
                """);
            result[table] = rows.Single();
        }

        return result;
    }

    /// <summary>The schema's columns, indexes and constraints as text, so two states can be compared exactly.</summary>
    private static async Task<SortedSet<string>> SchemaAsync(ScratchDatabase db)
    {
        var entries = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string row in await db.QueryAsync(
                     "SELECT 'column ' || table_name || '.' || column_name || ' ' || data_type || ' null=' || is_nullable || ' default=' || coalesce(column_default, '') FROM information_schema.columns WHERE table_schema = 'public'"))
        {
            entries.Add(row);
        }

        foreach (string row in await db.QueryAsync("SELECT 'index ' || indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'public'"))
        {
            entries.Add(row);
        }

        foreach (string row in await db.QueryAsync(
                     "SELECT 'constraint ' || conrelid::regclass::text || ' ' || conname || ' ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace = 'public'::regnamespace"))
        {
            entries.Add(row);
        }

        foreach (string row in await db.QueryAsync("SELECT 'table ' || tablename FROM pg_tables WHERE schemaname = 'public'"))
        {
            entries.Add(row);
        }

        return entries;
    }

    // ---- the tests --------------------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task Applying_the_migration_changes_no_existing_row_and_no_row_version()
    {
        await using ScratchDatabase db = await ScratchDatabase.CreateAsync();
        await db.MigrateAsync(Before);
        await SeedPreviousSchemaAsync(db);
        Dictionary<string, string> before = await FingerprintAsync(db);
        Assert.All(new[] { "Customers", "Vehicles", "Rentals", "Reservations", "Users" }, t => Assert.NotEqual("0", before[t].Split('|')[0]));

        await db.MigrateAsync(Expand);

        Dictionary<string, string> after = await FingerprintAsync(db);
        Assert.Equal(before, after); // same counts, same old-column data, same xmin on every row

        // The new structures exist and are empty, and every tenant column is NULL.
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM \"Companies\""));
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM \"CompanyMemberships\""));
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM \"CompanyCustomers\""));
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"CompanyId\" IS NOT NULL"));
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM \"Reservations\" WHERE \"CompanyId\" IS NOT NULL OR \"CompanyCustomerId\" IS NOT NULL"));
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM \"Rentals\" WHERE \"CompanyId\" IS NOT NULL OR \"CompanyCustomerId\" IS NOT NULL"));
    }

    [DatabaseFact]
    public async Task Every_existing_constraint_index_and_column_is_still_there_unchanged()
    {
        await using ScratchDatabase db = await ScratchDatabase.CreateAsync();
        await db.MigrateAsync(Before);
        SortedSet<string> before = await SchemaAsync(db);

        await db.MigrateAsync(Expand);
        SortedSet<string> after = await SchemaAsync(db);

        // Nothing that existed was altered or removed: the booking guarantees (exclusion constraint, one active rental
        // per vehicle, the check constraints, the global unique keys) are all byte-for-byte the same.
        Assert.Empty(before.Except(after));
        Assert.Contains(after, e => e.StartsWith("constraint \"Reservations\" EX_Reservations_NoOverlappingActive ", StringComparison.Ordinal));
        Assert.Contains(after, e => e.StartsWith("index UX_Rentals_ActiveRentalPerVehicle ", StringComparison.Ordinal));
        Assert.Contains(after, e => e.StartsWith("index IX_Vehicles_RegistrationNumber ", StringComparison.Ordinal));
        Assert.Contains(after, e => e.StartsWith("index IX_Customers_CustomerNumber ", StringComparison.Ordinal));

        // Only additions: the new tables, three nullable columns' worth of new columns, and new constraints.
        Assert.Contains("table Companies", after);
        Assert.Contains("table CompanyMemberships", after);
        Assert.Contains("table CompanyCustomers", after);
    }

    [DatabaseFact]
    public async Task Rolling_back_while_the_new_structures_are_empty_restores_the_exact_previous_schema_and_data()
    {
        await using ScratchDatabase db = await ScratchDatabase.CreateAsync();
        await db.MigrateAsync(Before);
        await SeedPreviousSchemaAsync(db);
        SortedSet<string> schemaBefore = await SchemaAsync(db);
        Dictionary<string, string> dataBefore = await FingerprintAsync(db);

        await db.MigrateAsync(Expand);
        await db.MigrateAsync(Before);

        Assert.Equal(schemaBefore, await SchemaAsync(db));
        Assert.Equal(dataBefore, await FingerprintAsync(db));
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename IN ('Companies', 'CompanyMemberships', 'CompanyCustomers')"));
    }

    [DatabaseFact]
    public async Task The_migration_can_be_applied_again_after_a_rollback()
    {
        await using ScratchDatabase db = await ScratchDatabase.CreateAsync();
        await db.MigrateAsync(Before);
        await db.MigrateAsync(Expand);
        await db.MigrateAsync(Before);
        await db.MigrateAsync(Expand);

        await using VehicleRentalDbContext context = db.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [DatabaseTheory]
    [InlineData("company")]
    [InlineData("membership")]
    [InlineData("customer-link")]
    [InlineData("vehicle-company")]
    [InlineData("reservation-customer-link")]
    public async Task Rolling_back_is_refused_once_any_company_or_tenant_data_exists_and_nothing_is_lost(string kind)
    {
        await using ScratchDatabase db = await ScratchDatabase.CreateAsync();
        await db.MigrateAsync(Before);
        await SeedPreviousSchemaAsync(db);
        await db.MigrateAsync(Expand);

        Guid company = Guid.NewGuid();
        await db.ExecuteAsync(
            "INSERT INTO \"Companies\" (\"Id\", \"Slug\", \"Name\", \"Status\", \"TimeZoneId\", \"CreatedAt\") VALUES (@id, 'acme', 'Acme', 'PendingReview', 'UTC', now())",
            ("id", company));

        switch (kind)
        {
            case "membership":
                Guid user = Guid.NewGuid();
                await db.ExecuteAsync(
                    "INSERT INTO \"Users\" (\"Id\", \"UserName\", \"AccessFailedCount\", \"EmailConfirmed\", \"LockoutEnabled\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\") VALUES (@id, 'u@example.test', 0, false, true, false, false)",
                    ("id", user));
                await db.ExecuteAsync(
                    "INSERT INTO \"CompanyMemberships\" (\"Id\", \"CompanyId\", \"UserId\", \"Role\", \"Status\", \"CreatedAt\") VALUES (@id, @c, @u, 'Owner', 'Active', now())",
                    ("id", Guid.NewGuid()),
                    ("c", company),
                    ("u", user));
                break;
            case "customer-link":
                await db.ExecuteAsync(
                    "INSERT INTO \"CompanyCustomers\" (\"Id\", \"CompanyId\", \"CustomerId\", \"CustomerNumber\", \"CreatedAt\") SELECT @id, @c, \"Id\", 'N-1', now() FROM \"Customers\" LIMIT 1",
                    ("id", Guid.NewGuid()),
                    ("c", company));
                break;
            case "vehicle-company":
                await db.ExecuteAsync("UPDATE \"Vehicles\" SET \"CompanyId\" = @c WHERE \"RegistrationNumber\" = 'CAR-001'", ("c", company));
                break;
            case "reservation-customer-link":
                await db.ExecuteAsync("UPDATE \"Reservations\" SET \"CompanyCustomerId\" = @x WHERE \"Status\" = 'Active'", ("x", Guid.NewGuid()));
                break;
        }

        if (kind is "vehicle-company" or "reservation-customer-link")
        {
            // These two kinds do not need the company row to be the thing that blocks the rollback.
            await db.ExecuteAsync("DELETE FROM \"Companies\" WHERE \"Id\" = @c AND NOT EXISTS (SELECT 1 FROM \"Vehicles\" WHERE \"CompanyId\" = @c)", ("c", company));
        }

        SortedSet<string> schemaBefore = await SchemaAsync(db);

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => db.MigrateAsync(Before));

        Assert.Equal("P0001", error.SqlState);
        Assert.Contains("Refusing to roll back", error.MessageText, StringComparison.Ordinal);
        Assert.Equal(schemaBefore, await SchemaAsync(db)); // the failed rollback left everything as it was
    }

    [DatabaseFact]
    public async Task The_migration_gives_up_after_the_lock_timeout_instead_of_waiting_behind_a_long_transaction_and_leaves_nothing_half_applied()
    {
        await using ScratchDatabase db = await ScratchDatabase.CreateAsync();
        await db.MigrateAsync(Before);
        SortedSet<string> schemaBefore = await SchemaAsync(db);

        // Another session holds a lock that blocks the migration's ALTER TABLE, like a long-running booking would.
        await using var blocker = new NpgsqlConnection(db.ConnectionString);
        await blocker.OpenAsync();
        await using NpgsqlTransaction transaction = await blocker.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("LOCK TABLE \"Vehicles\" IN ACCESS EXCLUSIVE MODE", blocker, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var stopwatch = Stopwatch.StartNew();
        InvalidOperationException wrapper = await Assert.ThrowsAsync<InvalidOperationException>(() => db.MigrateAsync(Expand));
        stopwatch.Stop();

        // The Npgsql execution strategy reports a lock timeout as a "transient failure" wrapping the database error.
        var error = Assert.IsType<PostgresException>(wrapper.InnerException);
        Assert.Equal("55P03", error.SqlState); // lock_not_available
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));

        await transaction.RollbackAsync();
        Assert.Equal(schemaBefore, await SchemaAsync(db));

        // Once the blocker is gone the same migration applies normally.
        await db.MigrateAsync(Expand);
        Assert.Equal(["0"], await db.QueryAsync("SELECT count(*) FROM \"Companies\""));
    }
}
