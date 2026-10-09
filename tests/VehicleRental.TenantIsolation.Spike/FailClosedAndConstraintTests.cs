using Npgsql;
using VehicleRental.TenantIsolation.Spike.Support;

namespace VehicleRental.TenantIsolation.Spike;

/// <summary>
/// What the database does when the tenant is missing or wrong, and what constraints can still reveal across companies.
/// Several tests here are CHARACTERISATIONS of unsafe shapes (they assert the unsafe thing happens) so the migration
/// knows exactly which shapes to avoid.
/// </summary>
public sealed class FailClosedAndConstraintTests : SpikeTestBase
{
    public FailClosedAndConstraintTests(SpikeDatabase database)
        : base(database)
    {
    }

    [SpikeFact]
    public async Task With_no_tenant_setting_nothing_is_readable_or_writable()
    {
        var (a, _) = await SeedAsync(3);
        await using NpgsqlConnection connection = await OpenAsync(AppPool(2));

        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM companies"));

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO vehicles (id, company_id, registration, daily_rate) VALUES (@id, @company, 'NOCTX-1', 1)",
            ("id", Guid.NewGuid()),
            ("company", a)));

        Assert.Equal("42501", error.SqlState);
    }

    [SpikeTheory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("' OR '1'='1")]
    [InlineData("1; DROP TABLE vehicles; --")]
    [InlineData("ffffffff-ffff-ffff-ffff-fffffffffffe")]
    public async Task An_empty_malformed_or_unknown_tenant_value_fails_closed(string value)
    {
        await SeedAsync(3);
        await using NpgsqlConnection connection = await OpenAsync(AppPool(2));
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "SELECT set_config('app.company_id', @value, true)", ("value", value));

        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM vehicles"));
        await transaction.RollbackAsync();
    }

    [SpikeFact]
    public async Task The_tenant_value_is_compared_as_a_uuid_so_letter_case_does_not_matter()
    {
        var (a, _) = await SeedAsync(3);

        long count = await AsTenantAsync<long>(AppPool(2), a, async (c, _) =>
        {
            await ExecuteAsync(c, "SELECT set_config('app.company_id', upper(@v), true)", ("v", a.ToString()));

            return await ScalarAsync<long>(c, "SELECT count(*) FROM vehicles");
        });

        Assert.Equal(3L, count);
    }

    [SpikeFact]
    public async Task Negative_control_a_registration_unique_across_all_companies_reveals_another_companys_fleet()
    {
        // Characterisation of the shape the migration must replace (production has this global unique index today).
        var (a, _) = await SeedAsync(0);
        var (b, _) = await SeedAsync(0);
        string pool = AppPool(2);

        await AsTenantAsync<int>(pool, a, (c, _) => ExecuteAsync(
            c, "INSERT INTO legacy_vehicles (id, company_id, registration) VALUES (@id, @company, 'SECRET-PLATE')", ("id", Guid.NewGuid()), ("company", a)));

        // Company B cannot SELECT A's vehicle, but the unique violation tells B that the plate exists elsewhere.
        PostgresException leak = await Assert.ThrowsAsync<PostgresException>(() => AsTenantAsync<int>(pool, b, (c, _) => ExecuteAsync(
            c, "INSERT INTO legacy_vehicles (id, company_id, registration) VALUES (@id, @company, 'SECRET-PLATE')", ("id", Guid.NewGuid()), ("company", b))));

        Assert.Equal("23505", leak.SqlState);
    }

    [SpikeFact]
    public async Task A_plain_foreign_key_ignores_RLS_and_lets_one_company_reference_anothers_row()
    {
        // Characterisation: the referential check runs with the table owner's rights, so it sees rows RLS hides.
        var (_, aVehicles) = await SeedAsync(1);
        var (b, _) = await SeedAsync(0);

        int inserted = await AsTenantAsync<int>(AppPool(2), b, (c, _) => ExecuteAsync(
            c,
            "INSERT INTO naive_reservations (id, company_id, vehicle_id) VALUES (@id, @company, @vehicle)",
            ("id", Guid.NewGuid()),
            ("company", b),
            ("vehicle", aVehicles[0])));

        Assert.Equal(1, inserted); // company B now points at company A's vehicle
    }

    [SpikeFact]
    public async Task A_composite_tenant_aware_foreign_key_refuses_the_cross_company_reference()
    {
        var (_, aVehicles) = await SeedAsync(1);
        var (b, _) = await SeedAsync(0);

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => AsTenantAsync<int>(AppPool(2), b, (c, _) => ExecuteAsync(
            c,
            "INSERT INTO reservations (id, company_id, vehicle_id, start_date, end_date, status) VALUES (@id, @company, @vehicle, '2031-02-01', '2031-02-03', 'Active')",
            ("id", Guid.NewGuid()),
            ("company", b),
            ("vehicle", aVehicles[0]))));

        Assert.Equal("23503", error.SqlState); // foreign_key_violation
    }

    [SpikeFact]
    public async Task The_no_overlap_exclusion_constraint_still_works_under_RLS_and_ignores_other_companies()
    {
        var (a, aVehicles) = await SeedAsync(1);
        var (b, bVehicles) = await SeedAsync(1);
        string pool = AppPool(2);
        const string insert =
            "INSERT INTO reservations (id, company_id, vehicle_id, start_date, end_date, status) VALUES (@id, @company, @vehicle, @start, @end, 'Active')";

        async Task<int> Reserve(Guid company, Guid vehicle, string start, string end) =>
            await AsTenantAsync<int>(pool, company, (c, _) => ExecuteAsync(
                c, insert, ("id", Guid.NewGuid()), ("company", company), ("vehicle", vehicle), ("start", DateOnly.Parse(start)), ("end", DateOnly.Parse(end))));

        await Reserve(a, aVehicles[0], "2031-03-01", "2031-03-05");
        await Reserve(a, aVehicles[0], "2031-03-05", "2031-03-08"); // adjacent: half-open, allowed
        await Reserve(b, bVehicles[0], "2031-03-01", "2031-03-05"); // same dates, another company's vehicle

        PostgresException overlap = await Assert.ThrowsAsync<PostgresException>(() => Reserve(a, aVehicles[0], "2031-03-04", "2031-03-06"));
        Assert.Equal("23P01", overlap.SqlState); // exclusion_violation
    }
}
