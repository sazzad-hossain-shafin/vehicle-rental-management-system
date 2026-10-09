using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.TenantIsolation.Spike.Support;
using VehicleRental.TenantIsolation.Spike.Tenancy;
using Xunit.Abstractions;

namespace VehicleRental.TenantIsolation.Spike;

/// <summary>Does the policy stay cheap? An indexed company filter, and the measured cost of a tenant transaction.</summary>
public sealed class PlanAndCostTests : SpikeTestBase
{
    private readonly ITestOutputHelper _output;

    public PlanAndCostTests(SpikeDatabase database, ITestOutputHelper output)
        : base(database)
    {
        _output = output;
    }

    [SpikeFact]
    public async Task The_policy_condition_can_use_the_company_index()
    {
        var (a, _) = await SeedAsync(300);
        await SeedAsync(300);

        string plan = await AsTenantAsync<string>(AppPool(2), a, async (c, _) =>
        {
            // The planner may prefer a sequential scan on a small table; forbid it to see whether the index CAN be used.
            await ExecuteAsync(c, "SET LOCAL enable_seqscan = off");
            await using var command = new NpgsqlCommand("EXPLAIN SELECT * FROM vehicles WHERE registration LIKE 'C%'", c);
            await using var reader = await command.ExecuteReaderAsync();
            var lines = new List<string>();

            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(0));
            }

            return string.Join(Environment.NewLine, lines);
        }) ?? "";

        _output.WriteLine(plan);
        Assert.Contains("Index", plan, StringComparison.Ordinal);
        Assert.Contains("company_id", plan, StringComparison.Ordinal);
    }

    [SpikeFact]
    public async Task Indicative_cost_of_a_tenant_transaction_per_unit_of_work()
    {
        // Not an assertion about speed (CI machines vary): a recorded measurement for the results document.
        var (a, _) = await SeedAsync(50);
        string pool = AppPool(maxPoolSize: 4);
        var session = new TenantSession(pool);
        const int iterations = 300;

        await session.RunAsync(a, db => db.Vehicles.CountAsync()); // warm up the pool and the model

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await session.RunAsync(a, db => db.Vehicles.OrderBy(v => v.Registration).Take(10).ToListAsync());
        }

        stopwatch.Stop();
        double tenantMs = stopwatch.Elapsed.TotalMilliseconds / iterations;

        // The same read as a superuser (exempt from RLS) without a tenant transaction, for comparison.
        await using NpgsqlConnection plain = await OpenAsync(Database.AdminConnectionString() + $";Application Name=spike-{Guid.NewGuid():N}");
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            await ScalarAsync<long>(plain, "SELECT count(*) FROM (SELECT * FROM vehicles WHERE company_id = @c ORDER BY registration LIMIT 10) x", ("c", a));
        }

        stopwatch.Stop();
        double plainMs = stopwatch.Elapsed.TotalMilliseconds / iterations;

        _output.WriteLine($"tenant transaction + EF read: {tenantMs:0.00} ms per unit of work");
        _output.WriteLine($"plain query, no transaction, no RLS: {plainMs:0.00} ms per query");

        Assert.True(tenantMs < 1000, "a unit of work should be far below a second even on a slow machine");
    }
}
