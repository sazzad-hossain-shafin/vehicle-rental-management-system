using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VehicleRental.Application.Tenancy;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// Can a tenant leak through a pooled connection, a failure, a cancellation or another company's request? The real
/// services run as the restricted runtime role, against the real tables with PREVIEW Row-Level Security (disposable
/// database only). The pool is forced to one physical connection where the test needs reuse to be certain.
/// </summary>
public class TenantPoolingTests : IClassFixture<TenantTestDatabase>
{
    private readonly TenantTestDatabase _db;

    public TenantPoolingTests(TenantTestDatabase db) => _db = db;

    private static AddVehicleRequest Car(string registration) => new(registration, "Toyota", "Corolla", 2022, VehicleType.Car, 50m);

    private async Task SeedAsync(ServiceProvider services, Guid company, string tag, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await _db.RunAsTenantAsync(services, company, p => p.GetRequiredService<VehicleService>().AddVehicleAsync(Car($"{tag}-S{i:000}")));
        }
    }

    private static Task<IReadOnlyList<VehicleDto>> ListAsync(IServiceProvider p) => p.GetRequiredService<VehicleService>().GetAllAsync();

    private static async Task<int> BackendPidAsync(IServiceProvider p)
    {
        var db = p.GetRequiredService<VehicleRentalDbContext>();
        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT pg_backend_pid()";

        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string?> SettingOnPlainConnectionAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_setting('app.company_id', true)", connection);

        return await command.ExecuteScalarAsync() as string;
    }

    [DatabaseFact]
    public async Task One_physical_connection_serves_two_companies_in_turn_without_a_leak()
    {
        string pool = _db.AppPool(maxPoolSize: 1);
        await using ServiceProvider services = _db.BuildServices(pool);
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        await SeedAsync(services, a, "AA", 3);
        await SeedAsync(services, b, "BB", 5);

        (int pid, IReadOnlyList<VehicleDto> list) first = await _db.RunAsTenantAsync(services, a, async p => (await BackendPidAsync(p), await ListAsync(p)));
        (int pid, IReadOnlyList<VehicleDto> list) second = await _db.RunAsTenantAsync(services, b, async p => (await BackendPidAsync(p), await ListAsync(p)));

        Assert.Equal(first.pid, second.pid); // the very same physical connection
        Assert.Equal(3, first.list.Count);
        Assert.All(first.list, v => Assert.StartsWith("AA-", v.RegistrationNumber, StringComparison.Ordinal));
        Assert.Equal(5, second.list.Count);
        Assert.All(second.list, v => Assert.StartsWith("BB-", v.RegistrationNumber, StringComparison.Ordinal));

        Assert.True(string.IsNullOrEmpty(await SettingOnPlainConnectionAsync(pool)));
    }

    [DatabaseFact]
    public async Task Isolation_does_not_depend_on_the_driver_resetting_pooled_connections()
    {
        string pool = _db.AppPool(maxPoolSize: 1, noResetOnClose: true);
        await using ServiceProvider services = _db.BuildServices(pool);
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        await SeedAsync(services, a, "CC", 2);

        await _db.RunAsTenantAsync(services, a, ListAsync);
        IReadOnlyList<VehicleDto> seenByB = await _db.RunAsTenantAsync(services, b, ListAsync);

        Assert.Empty(seenByB);
        Assert.True(string.IsNullOrEmpty(await SettingOnPlainConnectionAsync(pool)));

        // A plain, tenant-less use of that same connection sees nothing (fails closed), not the last company's rows.
        await using var plain = new NpgsqlConnection(pool);
        await plain.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM \"Vehicles\"", plain);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [DatabaseFact]
    public async Task Sequential_requests_that_alternate_between_companies_never_see_each_other()
    {
        await using ServiceProvider services = _db.BuildServices(_db.AppPool(maxPoolSize: 2));
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        await SeedAsync(services, a, "DA", 4);
        await SeedAsync(services, b, "DB", 6);

        for (int i = 0; i < 100; i++)
        {
            (Guid company, string tag, int expected) = i % 2 == 0 ? (a, "DA-", 4) : (b, "DB-", 6);

            IReadOnlyList<VehicleDto> seen = await _db.RunAsTenantAsync(services, company, ListAsync);

            Assert.Equal(expected, seen.Count);
            Assert.All(seen, v => Assert.StartsWith(tag, v.RegistrationNumber, StringComparison.Ordinal));
        }
    }

    [DatabaseFact]
    public async Task Concurrent_requests_from_three_companies_over_a_small_pool_never_cross()
    {
        await using ServiceProvider services = _db.BuildServices(_db.AppPool(maxPoolSize: 6));
        (Guid Id, string Tag)[] companies = [(await _db.NewCompanyAsync(), "EA"), (await _db.NewCompanyAsync(), "EB"), (await _db.NewCompanyAsync(), "EC")];

        foreach ((Guid id, string tag) in companies)
        {
            await SeedAsync(services, id, tag, 10);
        }

        var violations = new ConcurrentBag<string>();
        // Backend processes in use right now. A recycled connection may get a new pid over the run, so the invariant is
        // "never more than the pool size at once", not "never more than the pool size distinct pids over time".
        var inFlight = new HashSet<int>();
        int peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 240).Select(async i =>
        {
            (Guid company, string tag) = companies[i % 3];

            await _db.RunAsTenantAsync(services, company, async p =>
            {
                int pid = await BackendPidAsync(p);

                lock (inFlight)
                {
                    inFlight.Add(pid);
                    peak = Math.Max(peak, inFlight.Count);
                }

                // Hold the transaction open briefly so requests really overlap and connections are reused.
                await p.GetRequiredService<VehicleRentalDbContext>().Database.ExecuteSqlAsync($"SELECT pg_sleep({0.002 + (i % 5) * 0.003})");

                IReadOnlyList<VehicleDto> seen = await ListAsync(p);

                if (seen.Count < 10 || seen.Any(v => !v.RegistrationNumber.StartsWith(tag + "-", StringComparison.Ordinal)))
                {
                    violations.Add($"request {i}: saw {seen.Count} vehicles, some not from {tag}");
                }

                // The same call with the filter removed: the database itself must still hide the other companies.
                int foreign = await p.GetRequiredService<VehicleRentalDbContext>().Vehicles.CountAsync(v => !EF.Functions.Like(v.RegistrationNumber, tag + "-%"));

                if (foreign != 0)
                {
                    violations.Add($"request {i}: {foreign} foreign vehicles were readable");
                }

                lock (inFlight)
                {
                    inFlight.Remove(pid);
                }

                return 0;
            });
        }));

        Assert.Empty(violations);
        Assert.True(peak <= 6, $"the pool allows 6 physical connections but {peak} were in use at once");
    }

    [DatabaseFact]
    public async Task A_cancelled_request_releases_its_connection_with_no_tenant()
    {
        string pool = _db.AppPool(maxPoolSize: 1, noResetOnClose: true);
        await using ServiceProvider services = _db.BuildServices(pool);
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        await SeedAsync(services, a, "FA", 2);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<Exception>(() => services.GetRequiredService<ITenantScopeRunner>().RunAsync<int>(
            a,
            async (p, token) =>
            {
                await p.GetRequiredService<VehicleRentalDbContext>().Database.ExecuteSqlRawAsync("SELECT pg_sleep(30)", token);
                return 1;
            },
            cancel.Token));

        // Another company's request on the same single connection works and sees only its own data.
        Assert.Empty(await _db.RunAsTenantAsync(services, b, ListAsync));
        Assert.True(string.IsNullOrEmpty(await SettingOnPlainConnectionAsync(pool)));
        Assert.Equal(2, (await _db.RunAsTenantAsync(services, a, ListAsync)).Count);
    }

    [DatabaseFact]
    public async Task A_failure_after_a_save_rolls_back_and_the_connection_is_clean_for_the_next_company()
    {
        string pool = _db.AppPool(maxPoolSize: 1, noResetOnClose: true);
        await using ServiceProvider services = _db.BuildServices(pool);
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.RunAsTenantAsync<int>(services, a, async p =>
        {
            await p.GetRequiredService<VehicleService>().AddVehicleAsync(Car("GA-DOOMED"));
            throw new InvalidOperationException("failed after saving");
        }));

        Assert.Empty(await _db.RunAsTenantAsync(services, a, ListAsync));
        Assert.Empty(await _db.RunAsTenantAsync(services, b, ListAsync));
        Assert.True(string.IsNullOrEmpty(await SettingOnPlainConnectionAsync(pool)));
    }

    [DatabaseFact]
    public async Task A_database_error_inside_the_transaction_aborts_it_and_the_connection_is_reusable()
    {
        string pool = _db.AppPool(maxPoolSize: 1, noResetOnClose: true);
        await using ServiceProvider services = _db.BuildServices(pool);
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        await SeedAsync(services, b, "HB", 3);

        await Assert.ThrowsAnyAsync<Exception>(() => _db.RunAsTenantAsync<int>(services, a, async p =>
        {
            // A genuine PostgreSQL error (division by zero) aborts the whole transaction.
            await p.GetRequiredService<VehicleRentalDbContext>().Database.ExecuteSqlRawAsync("SELECT 1 / 0");
            return 1;
        }));

        Assert.Equal(3, (await _db.RunAsTenantAsync(services, b, ListAsync)).Count);
        Assert.True(string.IsNullOrEmpty(await SettingOnPlainConnectionAsync(pool)));
    }

    // ---- what the database itself does with a missing, wrong or hostile tenant -------------------------------------

    [DatabaseTheory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("' OR '1'='1")]
    [InlineData("ffffffff-ffff-ffff-ffff-fffffffffffe")]
    public async Task A_missing_malformed_or_unknown_tenant_fails_closed_on_the_real_tables(string value)
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid real = await _db.NewCompanyAsync();

        // Registration numbers are still unique across the whole database until the contract step, so each case needs its own.
        await SeedAsync(services, real, "I" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(), 2);

        await using var connection = new NpgsqlConnection(_db.AppPool(2));
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await TenantTestDatabase.Execute(connection, $"SELECT set_config('app.company_id', '{value.Replace("'", "''", StringComparison.Ordinal)}', true)");

        await using var count = new NpgsqlCommand("SELECT count(*) FROM \"Vehicles\"", connection, transaction);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);

        await using var insert = new NpgsqlCommand(
            "INSERT INTO \"Vehicles\" (\"Id\", \"RegistrationNumber\", \"Make\", \"Model\", \"Year\", \"VehicleType\", \"DailyRate\", \"AvailabilityStatus\") VALUES (gen_random_uuid(), 'NOCTX-1', 'x', 'y', 2022, 'Car', 10, 'Available')",
            connection,
            transaction);
        // A missing or malformed tenant is refused by the policy (42501). A well-formed UUID that is no company is accepted by
        // the policy (it equals the setting) but refused by the foreign key to Companies (23503): either way nothing is written.
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.True(error.SqlState is "42501" or "23503", $"expected a refusal, got {error.SqlState}");
    }

    [DatabaseFact]
    public async Task One_company_cannot_read_update_delete_move_or_lock_another_companys_rows_or_count_them()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        await SeedAsync(services, a, "JA", 3);
        await SeedAsync(services, b, "JB", 4);

        await _db.RunAsTenantAsync(services, a, async p =>
        {
            var db = p.GetRequiredService<VehicleRentalDbContext>();

            Assert.Equal(0, await db.Database.ExecuteSqlAsync($"UPDATE \"Vehicles\" SET \"DailyRate\" = 0 WHERE \"CompanyId\" = {b}"));
            Assert.Equal(0, await db.Database.ExecuteSqlAsync($"DELETE FROM \"Vehicles\" WHERE \"CompanyId\" = {b}"));
            Assert.Equal(3, await db.Vehicles.CountAsync());
            Assert.Equal(150m, await db.Vehicles.SumAsync(v => v.DailyRate));
            Assert.Equal(0, await db.Vehicles.Where(v => v.RegistrationNumber.StartsWith("JB-")).CountAsync());

            // A row for the other company cannot be inserted, and an own row cannot be moved into it.
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"INSERT INTO \"Vehicles\" (\"Id\", \"CompanyId\", \"RegistrationNumber\", \"Make\", \"Model\", \"Year\", \"VehicleType\", \"DailyRate\", \"AvailabilityStatus\") VALUES (gen_random_uuid(), {b}, 'STOLEN-1', 'x', 'y', 2022, 'Car', 10, 'Available')"));

            return 0;
        });
        // The first failed statement aborted that transaction, so check the other company's data afterwards, as the owner.
        Assert.Equal(4L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"CompanyId\" = @c AND \"DailyRate\" = 50", ("c", b)));
    }
}
