using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.TenantIsolation.Spike.Support;
using VehicleRental.TenantIsolation.Spike.Tenancy;

namespace VehicleRental.TenantIsolation.Spike;

/// <summary>Many companies at once, and every way a transaction (and so the tenant setting) can end.</summary>
public sealed class ConcurrencyAndLifecycleTests : SpikeTestBase
{
    public ConcurrencyAndLifecycleTests(SpikeDatabase database)
        : base(database)
    {
    }

    [SpikeFact]
    public async Task Concurrent_requests_from_three_companies_over_a_small_pool_never_cross()
    {
        (Guid Id, List<Guid> Vehicles)[] companies =
        [
            await SeedAsync(20),
            await SeedAsync(20),
            await SeedAsync(20),
        ];
        var session = new TenantSession(AppPool(maxPoolSize: 6));
        var pids = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
        var violations = new System.Collections.Concurrent.ConcurrentBag<string>();

        IEnumerable<Task> requests = Enumerable.Range(0, 300).Select(async i =>
        {
            (Guid company, _) = companies[i % 3];
            Guid[] foreign = companies.Where(c => c.Id != company).SelectMany(c => c.Vehicles).ToArray();

            await session.RunAsync(company, async db =>
            {
                pids[await BackendPidAsync(db)] = 0;

                // Hold the transaction open for a moment so requests genuinely overlap and connections are reused.
                double seconds = 0.002 + (i % 5) * 0.003;
                await db.Database.ExecuteSqlAsync($"SELECT pg_sleep({seconds})");

                // Same-company requests add vehicles as they commit, so the count grows from 20 up to 120.
                List<Vehicle> read = await db.Vehicles.ToListAsync();
                if (read.Count is < 20 or > 120 || read.Any(v => v.CompanyId != company))
                {
                    violations.Add($"request {i}: read {read.Count} rows, some from another company or an impossible count");
                }

                // Even with the EF filter removed, the database must not return another company's rows.
                int leaked = await db.Vehicles.IgnoreQueryFilters().CountAsync(v => foreign.Contains(v.Id));
                if (leaked != 0)
                {
                    violations.Add($"request {i}: IgnoreQueryFilters exposed {leaked} foreign rows");
                }

                db.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), CompanyId = company, Registration = $"X-{i}-{Guid.NewGuid():N}"[..12], DailyRate = 10 });
            });
        });

        await Task.WhenAll(requests);

        Assert.Empty(violations);
        Assert.True(pids.Count <= 6, $"the pool allows 6 physical connections but {pids.Count} were used");

        // 300 requests, 100 per company, each added exactly one vehicle to its own company and nothing else.
        foreach ((Guid id, List<Guid> _) in companies)
        {
            long count = await new TenantSession(AppPool(1)).RunAsync(id, db => db.Vehicles.LongCountAsync());
            Assert.Equal(20 + 100, count);
        }
    }

    [SpikeFact]
    public async Task An_exception_rolls_the_work_back_and_the_next_company_gets_a_clean_connection()
    {
        var (a, _) = await SeedAsync(2);
        var (b, _) = await SeedAsync(2);
        var session = new TenantSession(AppPool(maxPoolSize: 1));
        Guid doomed = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(a, async db =>
        {
            db.Vehicles.Add(new Vehicle { Id = doomed, CompanyId = a, Registration = "DOOMED-1", DailyRate = 1 });
            await db.SaveChangesAsync();
            throw new InvalidOperationException("the handler failed after saving");
        }));

        Assert.Equal(0, await session.RunAsync(a, db => db.Vehicles.CountAsync(v => v.Id == doomed)));
        Assert.Equal(2L, await session.RunAsync(b, db => db.Vehicles.LongCountAsync()));
    }

    [SpikeFact]
    public async Task A_database_error_aborts_the_transaction_and_the_connection_is_still_safe_to_reuse()
    {
        var (a, aVehicles) = await SeedAsync(2);
        var (b, _) = await SeedAsync(3);
        var session = new TenantSession(AppPool(maxPoolSize: 1));

        // Duplicate registration inside company A violates the per-company unique key.
        await Assert.ThrowsAsync<DbUpdateException>(() => session.RunAsync(a, async db =>
        {
            string existing = await db.Vehicles.Where(v => v.Id == aVehicles[0]).Select(v => v.Registration).SingleAsync();
            db.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), CompanyId = a, Registration = existing, DailyRate = 1 });
        }));

        Assert.Equal(3L, await session.RunAsync(b, db => db.Vehicles.LongCountAsync()));
    }

    [SpikeFact]
    public async Task A_cancelled_request_releases_the_connection_without_a_tenant()
    {
        var (a, _) = await SeedAsync(2);
        var (b, _) = await SeedAsync(4);
        string pool = AppPool(maxPoolSize: 1);
        var session = new TenantSession(pool);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<Exception>(() => session.RunAsync(
            a,
            db => db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(30)", cancel.Token),
            cancel.Token));

        Assert.Equal(4L, await session.RunAsync(b, db => db.Vehicles.LongCountAsync()));

        await using NpgsqlConnection connection = await OpenAsync(pool);
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles"));
    }

    [SpikeFact]
    public async Task Committing_discards_the_setting_so_the_connection_returns_to_the_pool_clean()
    {
        var (a, _) = await SeedAsync(2);
        string pool = AppPool(maxPoolSize: 1, noResetOnClose: true);
        var session = new TenantSession(pool);

        await session.RunAsync(a, db => db.Vehicles.CountAsync());

        await using NpgsqlConnection connection = await OpenAsync(pool);
        Assert.True(string.IsNullOrEmpty(await ScalarAsync<string>(connection, "SELECT current_setting('app.company_id', true)")));
    }

    [SpikeFact]
    public async Task A_single_statement_SaveChanges_without_an_explicit_transaction_is_still_tenant_scoped()
    {
        var (a, _) = await SeedAsync(1);
        var session = new TenantSession(AppPool(maxPoolSize: 2));
        Guid id = Guid.NewGuid();

        // No explicit transaction here. AutoTransactionBehavior.Always makes EF open one even for one statement, and the
        // interceptor sets the tenant inside it. (Without that setting EF sends the bare INSERT and the guard refuses it.)
        await using (TenantDbContext db = session.CreateContext(a))
        {
            db.Vehicles.Add(new Vehicle { Id = id, CompanyId = a, Registration = "IMPLICIT-1", DailyRate = 5 });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await session.RunAsync(a, db => db.Vehicles.CountAsync(v => v.Id == id)));
    }

    [SpikeFact]
    public async Task Reading_outside_a_tenant_transaction_is_refused_not_silently_empty()
    {
        var (a, _) = await SeedAsync(2);
        var session = new TenantSession(AppPool(maxPoolSize: 2));

        await using TenantDbContext db = session.CreateContext(a);

        await Assert.ThrowsAsync<TenantScopeException>(() => db.Vehicles.CountAsync());
    }

    [SpikeFact]
    public async Task A_context_with_no_company_may_not_run_anything()
    {
        var session = new TenantSession(AppPool(maxPoolSize: 2));

        await using TenantDbContext db = session.CreateContext(null);

        await Assert.ThrowsAsync<TenantScopeException>(() => db.Vehicles.CountAsync());
    }

    [SpikeFact]
    public async Task Tenant_scoped_code_may_not_switch_company_with_raw_SQL()
    {
        var (a, _) = await SeedAsync(1);
        var (b, _) = await SeedAsync(1);
        var session = new TenantSession(AppPool(maxPoolSize: 2));

        await Assert.ThrowsAsync<TenantScopeException>(() => session.RunAsync(a, db =>
            db.Database.ExecuteSqlAsync($"SELECT set_config('app.company_id', {b.ToString()}, true)")));

        await Assert.ThrowsAsync<TenantScopeException>(() => session.RunAsync(a, db =>
            db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off")));
    }
}
