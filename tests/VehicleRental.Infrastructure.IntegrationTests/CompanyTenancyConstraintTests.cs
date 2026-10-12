using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;
using VehicleRental.Infrastructure.IntegrationTests.Support;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The Phase 14A-3 structures (companies, memberships, customer links, nullable tenant columns): what the database
/// accepts and refuses. Raw SQL is used for the violations so the test hits the constraint, not the domain checks.
/// </summary>
public class CompanyTenancyConstraintTests : DatabaseTestBase
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public CompanyTenancyConstraintTests(PostgresFixture database)
        : base(database)
    {
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        object? result = await command.ExecuteScalarAsync();

        return result is null or DBNull ? default : (T)result;
    }

    private async Task<PostgresException> ExpectViolationAsync(string sql, params (string Name, object? Value)[] parameters) =>
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, parameters));

    private Task<int> InsertCompanyRawAsync(Guid id, string slug, string status = "Active", object? approvedAt = null, string name = "Acme", string zone = "UTC") =>
        ExecuteAsync(
            "INSERT INTO \"Companies\" (\"Id\", \"Slug\", \"Name\", \"Status\", \"TimeZoneId\", \"CreatedAt\", \"ApprovedAt\") VALUES (@id, @slug, @name, @status, @zone, now(), @approved)",
            ("id", id),
            ("slug", slug),
            ("name", name),
            ("status", status),
            ("zone", zone),
            ("approved", approvedAt ?? (status == "Active" ? DateTimeOffset.UtcNow : null)));

    private async Task<Guid> NewCompanyAsync(string slug)
    {
        Guid id = Guid.NewGuid();
        await InsertCompanyRawAsync(id, slug);

        return id;
    }

    private async Task<Guid> NewUserAsync(string name)
    {
        Guid id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"Users\" (\"Id\", \"UserName\", \"AccessFailedCount\", \"EmailConfirmed\", \"LockoutEnabled\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\") VALUES (@id, @name, 0, false, true, false, false)",
            ("id", id),
            ("name", name));

        return id;
    }

    private async Task<Guid> NewCustomerAsync(string number)
    {
        var customer = new Customer(number, "Customer " + number);
        await using var context = Database.CreateContext();
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        return customer.Id;
    }

    private Task<int> InsertMembershipRawAsync(Guid company, Guid user, string role = "Staff", string status = "Active") =>
        ExecuteAsync(
            "INSERT INTO \"CompanyMemberships\" (\"Id\", \"CompanyId\", \"UserId\", \"Role\", \"Status\", \"CreatedAt\") VALUES (@id, @company, @user, @role, @status, now())",
            ("id", Guid.NewGuid()),
            ("company", company),
            ("user", user),
            ("role", role),
            ("status", status));

    private Task<int> InsertLinkRawAsync(Guid company, Guid customer, string number) =>
        ExecuteAsync(
            "INSERT INTO \"CompanyCustomers\" (\"Id\", \"CompanyId\", \"CustomerId\", \"CustomerNumber\", \"CreatedAt\") VALUES (@id, @company, @customer, @number, now())",
            ("id", Guid.NewGuid()),
            ("company", company),
            ("customer", customer),
            ("number", number));

    // ---- companies ----------------------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task A_company_round_trips_through_EF_with_text_status_and_a_row_version()
    {
        var company = new Company("acme-rentals", "Acme Rentals", "Australia/Sydney", Now);
        company.Approve(Now);

        await using (var context = Database.CreateContext())
        {
            context.Companies.Add(company);
            await context.SaveChangesAsync();
        }

        await using var read = Database.CreateContext();
        Company loaded = await read.Companies.AsNoTracking().SingleAsync(c => c.Id == company.Id);

        Assert.Equal("acme-rentals", loaded.Slug);
        Assert.Equal(CompanyStatus.Active, loaded.Status);
        Assert.Equal("Australia/Sydney", loaded.TimeZoneId);
        Assert.Equal(Now, loaded.ApprovedAt);
        Assert.Equal("Active", await ScalarAsync<string>("SELECT \"Status\" FROM \"Companies\" WHERE \"Id\" = @id", ("id", company.Id)));
    }

    [DatabaseTheory]
    [InlineData("ab")]
    [InlineData("ABC")]
    [InlineData("-abc")]
    [InlineData("abc-")]
    [InlineData("a--b")]
    [InlineData("a b")]
    [InlineData("a_b")]
    [InlineData("a.b")]
    public async Task The_database_rejects_a_slug_with_a_bad_format(string slug)
    {
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => InsertCompanyRawAsync(Guid.NewGuid(), slug));

        Assert.Equal(CheckViolation, error.SqlState);
        Assert.Equal("CK_Companies_Slug_Format", error.ConstraintName);
    }

    [DatabaseFact]
    public async Task The_database_accepts_slugs_of_three_and_sixty_three_characters_and_rejects_sixty_four()
    {
        await InsertCompanyRawAsync(Guid.NewGuid(), "abc");
        await InsertCompanyRawAsync(Guid.NewGuid(), new string('a', 63));

        // The column itself is varchar(63), so PostgreSQL refuses the 64th character before the check constraint is reached.
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => InsertCompanyRawAsync(Guid.NewGuid(), new string('a', 64)));
        Assert.Equal("22001", error.SqlState);
    }

    [DatabaseFact]
    public async Task A_slug_collision_is_refused_by_a_named_unique_index_and_mapped_to_a_conflict()
    {
        await InsertCompanyRawAsync(Guid.NewGuid(), "acme");

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => InsertCompanyRawAsync(Guid.NewGuid(), "acme"));
        Assert.Equal(UniqueViolation, error.SqlState);
        Assert.Equal("UX_Companies_Slug", error.ConstraintName);

        // Through the unit of work the same collision becomes the application's conflict error, not a database error.
        await using var context = Database.CreateContext();
        context.Companies.Add(new Company("acme", "Another Acme", "UTC", Now));
        var conflict = await Assert.ThrowsAsync<VehicleRental.Application.Exceptions.ConflictException>(
            () => new UnitOfWork(context).SaveChangesAsync());
        Assert.Contains("web address", conflict.Message, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Status_must_be_known_and_consistent_with_approval()
    {
        Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertCompanyRawAsync(Guid.NewGuid(), "bad-status", "Deleted"))).SqlState);

        // Active needs an approval time; a pending company must not have one.
        PostgresException active = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO \"Companies\" (\"Id\", \"Slug\", \"Name\", \"Status\", \"TimeZoneId\", \"CreatedAt\") VALUES (@id, 'active-no-approval', 'x', 'Active', 'UTC', now())",
            ("id", Guid.NewGuid())));
        Assert.Equal("CK_Companies_ActiveIsApproved", active.ConstraintName);

        PostgresException pending = await Assert.ThrowsAsync<PostgresException>(() => InsertCompanyRawAsync(Guid.NewGuid(), "pending-approved", "PendingReview", DateTimeOffset.UtcNow));
        Assert.Equal("CK_Companies_PendingIsNotApproved", pending.ConstraintName);

        // Valid shapes: pending, active, approved-then-suspended and rejected (suspended, never approved).
        await InsertCompanyRawAsync(Guid.NewGuid(), "ok-pending", "PendingReview");
        await InsertCompanyRawAsync(Guid.NewGuid(), "ok-active", "Active");
        await InsertCompanyRawAsync(Guid.NewGuid(), "ok-suspended", "Suspended", DateTimeOffset.UtcNow);
        await InsertCompanyRawAsync(Guid.NewGuid(), "ok-rejected", "Suspended");
    }

    [DatabaseFact]
    public async Task A_blank_name_or_time_zone_is_refused()
    {
        Assert.Equal("CK_Companies_Name_NotBlank", (await Assert.ThrowsAsync<PostgresException>(() => InsertCompanyRawAsync(Guid.NewGuid(), "blank-name", name: "   "))).ConstraintName);
        Assert.Equal("CK_Companies_TimeZoneId_NotBlank", (await Assert.ThrowsAsync<PostgresException>(() => InsertCompanyRawAsync(Guid.NewGuid(), "blank-zone", zone: " "))).ConstraintName);
    }

    // ---- memberships --------------------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task A_user_has_at_most_one_membership_row_per_company_but_may_belong_to_several_companies()
    {
        Guid acme = await NewCompanyAsync("acme");
        Guid zeta = await NewCompanyAsync("zeta");
        Guid user = await NewUserAsync("owner@example.test");

        await InsertMembershipRawAsync(acme, user, "Owner");
        await InsertMembershipRawAsync(zeta, user, "Staff"); // another company: fine

        PostgresException duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipRawAsync(acme, user, "Staff"));
        Assert.Equal(UniqueViolation, duplicate.SqlState);
        Assert.Equal("UX_CompanyMemberships_CompanyId_UserId", duplicate.ConstraintName);

        // Removed memberships are history, not a free slot: the same user cannot get a second row.
        await ExecuteAsync("UPDATE \"CompanyMemberships\" SET \"Status\" = 'Removed' WHERE \"CompanyId\" = @c AND \"UserId\" = @u", ("c", acme), ("u", user));
        Assert.Equal(UniqueViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipRawAsync(acme, user, "Staff"))).SqlState);
    }

    [DatabaseFact]
    public async Task A_membership_role_and_status_must_be_known()
    {
        Guid company = await NewCompanyAsync("acme");
        Guid user = await NewUserAsync("u1@example.test");
        Guid other = await NewUserAsync("u2@example.test");

        Assert.Equal("CK_CompanyMemberships_Role_Known", (await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipRawAsync(company, user, "Admin"))).ConstraintName);
        Assert.Equal("CK_CompanyMemberships_Status_Known", (await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipRawAsync(company, other, "Staff", "Deleted"))).ConstraintName);
    }

    [DatabaseFact]
    public async Task Companies_and_users_with_memberships_cannot_be_deleted()
    {
        Guid company = await NewCompanyAsync("acme");
        Guid user = await NewUserAsync("u@example.test");
        await InsertMembershipRawAsync(company, user, "Owner");

        Assert.Equal(ForeignKeyViolation, (await ExpectViolationAsync("DELETE FROM \"Companies\" WHERE \"Id\" = @id", ("id", company))).SqlState);
        Assert.Equal(ForeignKeyViolation, (await ExpectViolationAsync("DELETE FROM \"Users\" WHERE \"Id\" = @id", ("id", user))).SqlState);
    }

    [DatabaseFact]
    public async Task A_membership_needs_a_real_company_and_user_and_a_known_adder()
    {
        Guid company = await NewCompanyAsync("acme");
        Guid user = await NewUserAsync("u@example.test");

        Assert.Equal(ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipRawAsync(Guid.NewGuid(), user))).SqlState);
        Assert.Equal(ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipRawAsync(company, Guid.NewGuid()))).SqlState);
        Assert.Equal(ForeignKeyViolation, (await ExpectViolationAsync(
            "INSERT INTO \"CompanyMemberships\" (\"Id\", \"CompanyId\", \"UserId\", \"Role\", \"Status\", \"CreatedAt\", \"CreatedByUserId\") VALUES (@id, @c, @u, 'Staff', 'Active', now(), @by)",
            ("id", Guid.NewGuid()),
            ("c", company),
            ("u", user),
            ("by", Guid.NewGuid()))).SqlState);
    }

    [DatabaseFact]
    public async Task A_membership_round_trips_through_EF()
    {
        Guid company = await NewCompanyAsync("acme");
        Guid user = await NewUserAsync("u@example.test");
        var membership = new CompanyMembership(company, user, CompanyRole.Owner, Now);

        await using (var context = Database.CreateContext())
        {
            context.CompanyMemberships.Add(membership);
            await context.SaveChangesAsync();
        }

        await using var read = Database.CreateContext();
        CompanyMembership loaded = await read.CompanyMemberships.AsNoTracking().SingleAsync(m => m.Id == membership.Id);

        Assert.Equal(CompanyRole.Owner, loaded.Role);
        Assert.Equal(MembershipStatus.Active, loaded.Status);
        Assert.Null(loaded.CreatedByUserId);
    }

    // ---- customer links -----------------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task A_customer_number_is_unique_within_a_company_but_may_repeat_across_companies()
    {
        Guid acme = await NewCompanyAsync("acme");
        Guid zeta = await NewCompanyAsync("zeta");
        Guid alice = await NewCustomerAsync("C1");
        Guid bob = await NewCustomerAsync("C2");

        await InsertLinkRawAsync(acme, alice, "N-100");
        await InsertLinkRawAsync(zeta, bob, "N-100"); // same number, another company: fine

        PostgresException duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertLinkRawAsync(acme, bob, "N-100"));
        Assert.Equal(UniqueViolation, duplicate.SqlState);
        Assert.Equal("UX_CompanyCustomers_CompanyId_CustomerNumber", duplicate.ConstraintName);
    }

    [DatabaseFact]
    public async Task One_person_has_one_link_per_company_and_can_be_linked_to_several_companies()
    {
        Guid acme = await NewCompanyAsync("acme");
        Guid zeta = await NewCompanyAsync("zeta");
        Guid alice = await NewCustomerAsync("C1");

        await InsertLinkRawAsync(acme, alice, "A-1");
        await InsertLinkRawAsync(zeta, alice, "Z-1"); // the same person at another company: fine

        PostgresException duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertLinkRawAsync(acme, alice, "A-2"));
        Assert.Equal("UX_CompanyCustomers_CompanyId_CustomerId", duplicate.ConstraintName);
    }

    [DatabaseFact]
    public async Task A_customer_with_a_link_cannot_be_deleted_and_a_link_needs_a_real_company_and_customer()
    {
        Guid company = await NewCompanyAsync("acme");
        Guid customer = await NewCustomerAsync("C1");
        await InsertLinkRawAsync(company, customer, "A-1");

        Assert.Equal(ForeignKeyViolation, (await ExpectViolationAsync("DELETE FROM \"Customers\" WHERE \"Id\" = @id", ("id", customer))).SqlState);
        Assert.Equal(ForeignKeyViolation, (await ExpectViolationAsync("DELETE FROM \"Companies\" WHERE \"Id\" = @id", ("id", company))).SqlState);
        Assert.Equal(ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertLinkRawAsync(Guid.NewGuid(), customer, "X-1"))).SqlState);
        Assert.Equal(ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertLinkRawAsync(company, Guid.NewGuid(), "X-2"))).SqlState);
    }

    [DatabaseFact]
    public async Task The_existing_global_customer_number_is_still_unique_until_the_contract_step()
    {
        await NewCustomerAsync("SAME");

        await using var context = Database.CreateContext();
        context.Customers.Add(new Customer("SAME", "Someone else"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
    }

    [DatabaseFact]
    public async Task A_customer_link_round_trips_through_EF()
    {
        Guid company = await NewCompanyAsync("acme");
        Guid customer = await NewCustomerAsync("C1");
        var link = new CompanyCustomer(company, customer, "a-7", Now);

        await using (var context = Database.CreateContext())
        {
            context.CompanyCustomers.Add(link);
            await context.SaveChangesAsync();
        }

        await using var read = Database.CreateContext();
        CompanyCustomer loaded = await read.CompanyCustomers.AsNoTracking().SingleAsync(c => c.Id == link.Id);

        Assert.Equal("A-7", loaded.CustomerNumber);
    }

    // ---- the nullable tenant columns on the existing tables -----------------------------------------------------

    [DatabaseFact]
    public async Task Existing_aggregates_save_and_load_exactly_as_before_and_the_new_columns_stay_null()
    {
        var vehicle = TestEntities.NewVehicle("EXP-100");
        var customer = TestEntities.NewCustomer("CUST-EXP");
        var rental = TestEntities.NewRental(vehicle, customer);
        await TestEntities.SaveAsync(Database, vehicle, customer, rental);

        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM \"Vehicles\" WHERE \"CompanyId\" IS NOT NULL"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM \"Rentals\" WHERE \"CompanyId\" IS NOT NULL OR \"CompanyCustomerId\" IS NOT NULL"));

        await using var read = Database.CreateContext();
        Assert.Equal("EXP-100", (await read.Vehicles.SingleAsync()).RegistrationNumber);
    }

    [DatabaseFact]
    public async Task Vehicles_Reservations_and_Rentals_may_name_a_real_company_but_not_a_missing_one()
    {
        Guid company = await NewCompanyAsync("acme");
        var vehicle = TestEntities.NewVehicle("EXP-200");
        var customer = TestEntities.NewCustomer("CUST-200");
        var rental = TestEntities.NewRental(vehicle, customer);
        var reservation = Reservation.Create(
            customer,
            vehicle,
            new DateOnly(2031, 1, 10),
            new DateOnly(2031, 1, 13),
            new DateOnly(2031, 1, 1),
            Now,
            new NormalPricingStrategy());

        await using (var context = Database.CreateContext())
        {
            context.AddRange(vehicle, customer, rental, reservation);
            await context.SaveChangesAsync();
        }

        foreach ((string table, Guid id) in new[] { ("Vehicles", vehicle.Id), ("Rentals", rental.Id), ("Reservations", reservation.Id) })
        {
            // A real company is accepted.
            Assert.Equal(1, await ExecuteAsync($"UPDATE \"{table}\" SET \"CompanyId\" = @c WHERE \"Id\" = @id", ("c", company), ("id", id)));

            // A missing company is refused, by a named foreign key.
            PostgresException error = await ExpectViolationAsync($"UPDATE \"{table}\" SET \"CompanyId\" = @c WHERE \"Id\" = @id", ("c", Guid.NewGuid()), ("id", id));
            Assert.Equal(ForeignKeyViolation, error.SqlState);
            Assert.Equal($"FK_{table}_Companies_CompanyId", error.ConstraintName);
        }

        // The company is now referenced by tenant rows and cannot be deleted.
        Assert.Equal(ForeignKeyViolation, (await ExpectViolationAsync("DELETE FROM \"Companies\" WHERE \"Id\" = @id", ("id", company))).SqlState);
    }

    [DatabaseFact]
    public async Task CompanyCustomerId_exists_on_Reservations_and_Rentals_as_a_plain_nullable_column()
    {
        foreach (string table in new[] { "Reservations", "Rentals" })
        {
            Assert.Equal(
                "YES",
                await ScalarAsync<string>(
                    "SELECT is_nullable FROM information_schema.columns WHERE table_name = @t AND column_name = 'CompanyCustomerId'",
                    ("t", table)));
        }

        // Its composite foreign key (CompanyId, CompanyCustomerId) needs the backfill first, so it is not added yet.
        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                """
                SELECT count(*) FROM pg_constraint c
                JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
                WHERE c.contype = 'f' AND a.attname = 'CompanyCustomerId'
                """));
    }

    [DatabaseFact]
    public async Task Vehicles_has_the_unique_CompanyId_Id_index_the_contract_step_will_reference()
    {
        string? definition = await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'UX_Vehicles_CompanyId_Id'");

        Assert.NotNull(definition);
        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("(\"CompanyId\", \"Id\")", definition, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task The_phase_adds_only_the_reviewed_indexes_and_none_on_the_always_null_tenant_columns()
    {
        string[] expected =
        [
            "IX_CompanyCustomers_CustomerId",
            "IX_CompanyMemberships_ActiveOwners",
            "IX_CompanyMemberships_UserId_Status",
            "UX_Companies_Slug",
            "UX_CompanyCustomers_CompanyId_CustomerId",
            "UX_CompanyCustomers_CompanyId_CustomerNumber",
            "UX_CompanyCustomers_CompanyId_Id",
            "UX_CompanyMemberships_CompanyId_UserId",
            "UX_Vehicles_CompanyId_Id",
        ];

        List<string> actual = [];
        await using (var connection = new NpgsqlConnection(Database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT indexname FROM pg_indexes
                WHERE schemaname = 'public'
                  AND (tablename IN ('Companies', 'CompanyMemberships', 'CompanyCustomers')
                       OR indexname IN ('UX_Vehicles_CompanyId_Id')
                       OR indexdef LIKE '%("CompanyId")%' OR indexdef LIKE '%("CreatedByUserId")%')
                  AND indexname NOT LIKE 'PK\_%'
                ORDER BY indexname
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                actual.Add(reader.GetString(0));
            }
        }

        Assert.Equal(expected, actual);
    }

    [DatabaseFact]
    public async Task The_booking_guarantees_are_still_in_the_schema_unchanged()
    {
        Assert.Equal(
            1L,
            await ScalarAsync<long>("SELECT count(*) FROM pg_constraint WHERE conname = 'EX_Reservations_NoOverlappingActive' AND contype = 'x'"));
        string rentalIndex = await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'UX_Rentals_ActiveRentalPerVehicle'") ?? "";
        Assert.Contains("UNIQUE", rentalIndex, StringComparison.Ordinal);
        Assert.Contains("'Active'", rentalIndex, StringComparison.Ordinal);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'"));
    }
}
