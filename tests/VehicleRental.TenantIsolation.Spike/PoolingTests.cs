using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.TenantIsolation.Spike.Support;
using VehicleRental.TenantIsolation.Spike.Tenancy;

namespace VehicleRental.TenantIsolation.Spike;

/// <summary>
/// Can a tenant leak through a pooled connection? The pool is forced down to a single physical connection so every
/// request provably reuses the previous request's connection (same backend process ID).
/// </summary>
public sealed class PoolingTests : SpikeTestBase
{
    public PoolingTests(SpikeDatabase database)
        : base(database)
    {
    }

    [SpikeFact]
    public async Task One_physical_connection_serves_two_companies_in_turn_without_a_leak()
    {
        var (a, _) = await SeedAsync(3);
        var (b, _) = await SeedAsync(7);
        string pool = AppPool(maxPoolSize: 1);
        var session = new TenantSession(pool);

        (int pid, int count, bool onlyA) first = await session.RunAsync(a, async db =>
            (await BackendPidAsync(db), await db.Vehicles.CountAsync(), await db.Vehicles.AllAsync(v => v.CompanyId == a)));
        (int pid, int count, bool onlyB) second = await session.RunAsync(b, async db =>
            (await BackendPidAsync(db), await db.Vehicles.CountAsync(), await db.Vehicles.AllAsync(v => v.CompanyId == b)));

        Assert.Equal(first.pid, second.pid); // the very same physical connection
        Assert.Equal(3, first.count);
        Assert.True(first.onlyA);
        Assert.Equal(7, second.count);
        Assert.True(second.onlyB);

        // A plain use of the same connection afterwards carries no tenant: the setting died with the transaction.
        await using NpgsqlConnection connection = await OpenAsync(pool);
        Assert.Equal(first.pid, await ScalarAsync<int>(connection, "SELECT pg_backend_pid()"));
        Assert.True(string.IsNullOrEmpty(await ScalarAsync<string>(connection, "SELECT current_setting('app.company_id', true)")));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles"));
    }

    [SpikeFact]
    public async Task Sequential_requests_that_alternate_between_companies_never_see_each_other()
    {
        var (a, aVehicles) = await SeedAsync(4);
        var (b, bVehicles) = await SeedAsync(6);
        var session = new TenantSession(AppPool(maxPoolSize: 2));

        for (int i = 0; i < 100; i++)
        {
            (Guid company, List<Guid> own, List<Guid> other) = i % 2 == 0 ? (a, aVehicles, bVehicles) : (b, bVehicles, aVehicles);

            List<Guid> seen = await session.RunAsync(company, db => db.Vehicles.Select(v => v.Id).ToListAsync());

            Assert.Equal(own.Order(), seen.Order());
            Assert.DoesNotContain(seen, other.Contains);
        }
    }

    [SpikeFact]
    public async Task Isolation_does_not_depend_on_the_driver_resetting_pooled_connections()
    {
        // "No Reset On Close" removes Npgsql's DISCARD ALL. A transaction-local setting must still not leak.
        var (a, _) = await SeedAsync(3);
        var (b, _) = await SeedAsync(2);
        string pool = AppPool(maxPoolSize: 1, noResetOnClose: true);
        var session = new TenantSession(pool);

        int pidA = await session.RunAsync(a, BackendPidAsync);
        long seenByB = await session.RunAsync(b, db => db.Vehicles.LongCountAsync());
        int pidB = await session.RunAsync(b, BackendPidAsync);

        Assert.Equal(pidA, pidB);
        Assert.Equal(2L, seenByB);

        await using NpgsqlConnection connection = await OpenAsync(pool);
        Assert.Equal(pidA, await ScalarAsync<int>(connection, "SELECT pg_backend_pid()"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles"));
    }

    [SpikeFact]
    public async Task Negative_control_a_session_scoped_setting_DOES_leak_when_the_driver_does_not_reset()
    {
        // This is the design the spike rejects. It exists to prove the harness can see a leak when there is one.
        var (a, _) = await SeedAsync(3);
        string pool = AppPool(maxPoolSize: 1, noResetOnClose: true);

        await using (NpgsqlConnection first = await OpenAsync(pool))
        {
            await ExecuteAsync(first, "SELECT set_config('app.company_id', @company, false)", ("company", a.ToString()));
        } // returned to the pool, still carrying the session-scoped setting

        await using NpgsqlConnection next = await OpenAsync(pool);
        long leaked = await ScalarAsync<long>(next, "SELECT count(*) FROM vehicles");

        Assert.Equal(3L, leaked); // a request that never named a company can read company A
    }

    [SpikeFact]
    public async Task A_session_scoped_setting_is_cleared_only_by_the_drivers_reset()
    {
        // With Npgsql's default reset, the same mistake is masked: documented so nobody mistakes luck for safety.
        var (a, _) = await SeedAsync(3);
        string pool = AppPool(maxPoolSize: 1);

        await using (NpgsqlConnection first = await OpenAsync(pool))
        {
            await ExecuteAsync(first, "SELECT set_config('app.company_id', @company, false)", ("company", a.ToString()));
        }

        await using NpgsqlConnection next = await OpenAsync(pool);

        Assert.Equal(0L, await ScalarAsync<long>(next, "SELECT count(*) FROM vehicles"));
    }
}
