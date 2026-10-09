using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.TenantIsolation.Spike.Support;
using VehicleRental.TenantIsolation.Spike.Tenancy;

namespace VehicleRental.TenantIsolation.Spike;

/// <summary>
/// Everything company A might try against company B's rows, as the application role. Each test names the attack.
/// Raw SQL is used on purpose: it is what a missing EF filter or a hand-written query would send.
/// </summary>
public sealed class CrossTenantTests : SpikeTestBase
{
    public CrossTenantTests(SpikeDatabase database)
        : base(database)
    {
    }

    [SpikeFact]
    public async Task Reading_another_companys_rows_by_id_finds_nothing_even_without_the_EF_filter()
    {
        var (a, _) = await SeedAsync(2);
        var (_, bVehicles) = await SeedAsync(2);
        var session = new TenantSession(AppPool(maxPoolSize: 2));

        Vehicle? viaEf = await session.RunAsync(a, db => db.Vehicles.IgnoreQueryFilters().SingleOrDefaultAsync(v => v.Id == bVehicles[0]));
        long viaSql = await AsTenantAsync<long>(AppPool(2), a, (c, _) =>
            ScalarAsync<long>(c, "SELECT count(*) FROM vehicles WHERE id = @id", ("id", bVehicles[0])));

        Assert.Null(viaEf);
        Assert.Equal(0L, viaSql);
    }

    [SpikeFact]
    public async Task Updating_and_deleting_another_companys_rows_affects_nothing()
    {
        var (a, _) = await SeedAsync(2);
        var (b, bVehicles) = await SeedAsync(3);
        string pool = AppPool(2);

        (int updated, int deleted) = await AsTenantAsync<(int, int)>(pool, a, async (c, _) =>
        {
            int u = await ExecuteAsync(c, "UPDATE vehicles SET daily_rate = 0 WHERE id = @id OR company_id = @b", ("id", bVehicles[0]), ("b", b));
            int d = await ExecuteAsync(c, "DELETE FROM vehicles WHERE id = @id OR company_id = @b", ("id", bVehicles[0]), ("b", b));

            return (u, d);
        });

        Assert.Equal(0, updated);
        Assert.Equal(0, deleted);

        // B's data is intact, checked as B.
        long remaining = await new TenantSession(pool).RunAsync(b, db => db.Vehicles.LongCountAsync(v => v.DailyRate > 0));
        Assert.Equal(3L, remaining);
    }

    [SpikeFact]
    public async Task Inserting_a_row_for_another_company_is_refused()
    {
        var (a, _) = await SeedAsync(1);
        var (b, _) = await SeedAsync(1);

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => AsTenantAsync<int>(AppPool(2), a, async (c, _) =>
            await ExecuteAsync(
                c,
                "INSERT INTO vehicles (id, company_id, registration, daily_rate) VALUES (@id, @company, 'STOLEN-1', 1)",
                ("id", Guid.NewGuid()),
                ("company", b))));

        Assert.Equal("42501", error.SqlState); // new row violates row-level security policy
    }

    [SpikeFact]
    public async Task Moving_a_row_into_another_company_with_an_update_is_refused()
    {
        var (a, aVehicles) = await SeedAsync(1);
        var (b, _) = await SeedAsync(1);

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => AsTenantAsync<int>(AppPool(2), a, async (c, _) =>
            await ExecuteAsync(c, "UPDATE vehicles SET company_id = @b WHERE id = @id", ("b", b), ("id", aVehicles[0]))));

        Assert.Equal("42501", error.SqlState);
    }

    [SpikeFact]
    public async Task Aggregates_joins_and_subqueries_only_ever_cover_the_callers_company()
    {
        var (a, aVehicles) = await SeedAsync(4);
        var (b, bVehicles) = await SeedAsync(9);
        string pool = AppPool(2);

        // A reservation for each company so joins have something to join.
        await SeedReservationAsync(a, aVehicles[0], new DateOnly(2031, 1, 1), new DateOnly(2031, 1, 4));
        await SeedReservationAsync(b, bVehicles[0], new DateOnly(2031, 1, 1), new DateOnly(2031, 1, 4));
        await SeedReservationAsync(b, bVehicles[1], new DateOnly(2031, 1, 1), new DateOnly(2031, 1, 4));

        (long vehicles, decimal sum, long groups, long joined, bool exists) = await AsTenantAsync<(long, decimal, long, long, bool)>(pool, a, async (c, _) =>
        (
            await ScalarAsync<long>(c, "SELECT count(*) FROM vehicles"),
            await ScalarAsync<decimal>(c, "SELECT sum(daily_rate) FROM vehicles"),
            await ScalarAsync<long>(c, "SELECT count(DISTINCT company_id) FROM vehicles"),
            await ScalarAsync<long>(c, "SELECT count(*) FROM reservations r JOIN vehicles v ON v.id = r.vehicle_id"),
            await ScalarAsync<bool>(c, "SELECT EXISTS (SELECT 1 FROM vehicles WHERE company_id <> @a)", ("a", a))
        ));

        Assert.Equal(4L, vehicles);
        Assert.Equal(40m + 41m + 42m + 43m, sum);
        Assert.Equal(1L, groups);
        Assert.Equal(1L, joined);
        Assert.False(exists);
    }

    [SpikeFact]
    public async Task Locking_another_companys_vehicle_row_finds_nothing_to_lock()
    {
        var (a, _) = await SeedAsync(1);
        var (_, bVehicles) = await SeedAsync(1);

        long locked = await AsTenantAsync<long>(AppPool(2), a, async (c, _) =>
        {
            await using var command = new NpgsqlCommand("SELECT id FROM vehicles WHERE id = @id FOR UPDATE", c);
            command.Parameters.AddWithValue("id", bVehicles[0]);
            await using var reader = await command.ExecuteReaderAsync();
            long rows = 0;

            while (await reader.ReadAsync())
            {
                rows++;
            }

            return rows;
        });

        Assert.Equal(0L, locked);
    }

    [SpikeFact]
    public async Task The_vehicle_row_lock_still_serialises_bookings_of_one_vehicle_inside_a_tenant_transaction()
    {
        // The production booking guarantee (ADR 005) is SELECT ... FOR UPDATE on the vehicle row. It must keep working.
        var (a, aVehicles) = await SeedAsync(1);
        string pool = AppPool(4);

        await using NpgsqlConnection first = await OpenAsync(pool);
        await using NpgsqlTransaction firstTx = await first.BeginTransactionAsync();
        await SpikeDatabase.SetTenant(first, firstTx, a);
        Assert.Equal(aVehicles[0], await ScalarAsync<Guid>(first, "SELECT id FROM vehicles WHERE id = @id FOR UPDATE", ("id", aVehicles[0])));

        await using NpgsqlConnection second = await OpenAsync(pool);
        await using NpgsqlTransaction secondTx = await second.BeginTransactionAsync();
        await SpikeDatabase.SetTenant(second, secondTx, a);
        await ExecuteAsync(second, "SET LOCAL lock_timeout = '400ms'");

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() =>
            ScalarAsync<Guid>(second, "SELECT id FROM vehicles WHERE id = @id FOR UPDATE", ("id", aVehicles[0])));

        Assert.Equal("55P03", error.SqlState); // lock_not_available: the second booking must wait or give up
    }

    [SpikeFact]
    public async Task Two_companies_may_use_the_same_registration_number_but_one_company_may_not_repeat_it()
    {
        var (a, _) = await SeedAsync(0);
        var (b, _) = await SeedAsync(0);
        var session = new TenantSession(AppPool(2));

        await session.RunAsync(a, db => { db.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), CompanyId = a, Registration = "SAME-1", DailyRate = 1 }); return Task.CompletedTask; });
        await session.RunAsync(b, db => { db.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), CompanyId = b, Registration = "SAME-1", DailyRate = 1 }); return Task.CompletedTask; });

        await Assert.ThrowsAsync<DbUpdateException>(() => session.RunAsync(a, db =>
        {
            db.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), CompanyId = a, Registration = "SAME-1", DailyRate = 1 });
            return Task.CompletedTask;
        }));
    }

    private async Task SeedReservationAsync(Guid company, Guid vehicle, DateOnly start, DateOnly end)
    {
        await using NpgsqlConnection owner = await OpenAsync(Database.OwnerConnectionString());
        await using NpgsqlTransaction transaction = await owner.BeginTransactionAsync();
        await SpikeDatabase.SetTenant(owner, transaction, company);
        await ExecuteAsync(
            owner,
            "INSERT INTO reservations (id, company_id, vehicle_id, start_date, end_date, status) VALUES (@id, @company, @vehicle, @start, @end, 'Active')",
            ("id", Guid.NewGuid()),
            ("company", company),
            ("vehicle", vehicle),
            ("start", start),
            ("end", end));
        await transaction.CommitAsync();
    }
}
