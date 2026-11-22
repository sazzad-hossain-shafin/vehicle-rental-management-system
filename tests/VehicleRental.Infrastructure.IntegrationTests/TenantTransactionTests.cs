using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Tenancy;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The tenant transaction against the real EF model, the real services and the runtime role, with PREVIEW Row-Level
/// Security on the three tenant tables (in this disposable database only). The order under test: bind the tenant,
/// begin the transaction, apply the transaction-local setting, run the queries, commit or roll back.
/// </summary>
public class TenantTransactionTests : IClassFixture<TenantTestDatabase>
{
    private readonly TenantTestDatabase _db;

    public TenantTransactionTests(TenantTestDatabase db) => _db = db;

    private static AddVehicleRequest Car(string registration) => new(registration, "Toyota", "Corolla", 2022, VehicleType.Car, 50m);

    private static string Reg() => "T-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<string?> SettingAsync(DbConnection connection, DbTransaction? transaction = null)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT current_setting('app.company_id', true)";

        return await command.ExecuteScalarAsync() as string;
    }

    // ---- ordering ----------------------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task The_tenant_setting_is_in_the_transaction_before_the_first_query_and_gone_after_commit()
    {
        await using ServiceProvider services = _db.BuildServices(_db.AppPool(maxPoolSize: 1, noResetOnClose: true));
        Guid company = await _db.NewCompanyAsync();

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(company, TenantSource.Background);
            var db = scope.ServiceProvider.GetRequiredService<VehicleRentalDbContext>();
            var transaction = scope.ServiceProvider.GetRequiredService<ITenantTransaction>();

            // 1. bound, but no transaction yet: every command is refused, not answered with nothing.
            await Assert.ThrowsAsync<TenantRequiredException>(() => db.Vehicles.CountAsync());

            await transaction.BeginAsync(); // 2 and 3

            // 4. the very first thing in the transaction already carries the tenant.
            DbTransaction current = db.Database.CurrentTransaction!.GetDbTransaction();
            Assert.Equal(company.ToString(), await SettingAsync(db.Database.GetDbConnection(), current));
            Assert.Equal(0, await db.Vehicles.CountAsync());

            await transaction.CommitAsync(); // 5
            Assert.False(transaction.IsActive);
        }

        // The same physical connection (pool of one, driver reset OFF) carries no tenant afterwards.
        await using var plain = new NpgsqlConnection(_db.AppPool(1, noResetOnClose: true));
        await plain.OpenAsync();
        Assert.True(string.IsNullOrEmpty(await SettingAsync(plain)));
    }

    [DatabaseFact]
    public async Task Beginning_without_a_bound_tenant_or_with_tenancy_disabled_is_refused_and_opens_nothing()
    {
        await using (ServiceProvider services = _db.BuildServices())
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            var transaction = scope.ServiceProvider.GetRequiredService<ITenantTransaction>();
            await Assert.ThrowsAsync<TenantRequiredException>(() => transaction.BeginAsync());
            Assert.False(transaction.IsActive);
            Assert.Null(scope.ServiceProvider.GetRequiredService<VehicleRentalDbContext>().Database.CurrentTransaction);
        }

        await using (ServiceProvider disabled = _db.BuildServices(tenancyEnabled: false))
        await using (AsyncServiceScope scope = disabled.CreateAsyncScope())
        {
            Assert.Throws<TenancyDisabledException>(() => scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(Guid.NewGuid(), TenantSource.Background));
            await Assert.ThrowsAsync<TenancyDisabledException>(() => scope.ServiceProvider.GetRequiredService<ITenantTransaction>().BeginAsync());
        }
    }

    [DatabaseFact]
    public async Task A_second_transaction_cannot_be_opened_and_the_tenant_cannot_change_after_binding()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid first = await _db.NewCompanyAsync();
        Guid second = await _db.NewCompanyAsync();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var binder = scope.ServiceProvider.GetRequiredService<ITenantBinder>();
        var transaction = scope.ServiceProvider.GetRequiredService<ITenantTransaction>();
        binder.Bind(first, TenantSource.Membership);
        await transaction.BeginAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.BeginAsync());
        Assert.Throws<TenantRebindException>(() => binder.Bind(second, TenantSource.Membership));
        Assert.Equal(first, scope.ServiceProvider.GetRequiredService<ITenantContext>().CompanyId);

        await transaction.RollbackAsync();
    }

    [DatabaseFact]
    public async Task A_transaction_started_before_the_tenant_was_bound_is_never_trusted()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VehicleRentalDbContext>();

        await using var early = await db.Database.BeginTransactionAsync(); // no tenant yet: no setting applied
        scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(company, TenantSource.PublicSlug);

        // Reads on that transaction are refused (the tenant setting was never applied to it) ...
        await Assert.ThrowsAsync<TenantRequiredException>(() => db.Vehicles.CountAsync());

        // ... and a tenant transaction cannot be opened on top of it either.
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ITenantTransaction>().BeginAsync());
    }

    // ---- the guards --------------------------------------------------------------------------------------------

    [DatabaseFact]
    public async Task A_context_with_no_tenant_is_not_touched_by_the_guards_at_all()
    {
        await using ServiceProvider services = _db.BuildServices();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VehicleRentalDbContext>();

        // No tenant, no transaction: exactly the behaviour every existing code path has. Row-Level Security (preview) then
        // answers with nothing, but the command itself is allowed to run.
        Assert.Equal(0, await db.Vehicles.CountAsync());
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        Assert.True(await db.Database.CanConnectAsync());
    }

    [DatabaseFact]
    public async Task Raw_sql_that_touches_the_tenant_setting_the_role_or_row_security_is_refused_inside_the_transaction()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid mine = await _db.NewCompanyAsync();
        Guid other = await _db.NewCompanyAsync();

        await Assert.ThrowsAsync<TenantRequiredException>(() => _db.RunAsTenantAsync(services, mine, provider =>
            provider.GetRequiredService<VehicleRentalDbContext>().Database.ExecuteSqlAsync($"SELECT set_config('app.company_id', {other.ToString()}, true)")));
        await Assert.ThrowsAsync<TenantRequiredException>(() => _db.RunAsTenantAsync(services, mine, provider =>
            provider.GetRequiredService<VehicleRentalDbContext>().Database.ExecuteSqlRawAsync("SET LOCAL row_security = off")));
        await Assert.ThrowsAsync<TenantRequiredException>(() => _db.RunAsTenantAsync(services, mine, provider =>
            provider.GetRequiredService<VehicleRentalDbContext>().Database.ExecuteSqlRawAsync("SET ROLE postgres")));
    }

    [DatabaseFact]
    public async Task Writing_outside_the_tenant_transaction_is_refused_even_for_a_single_statement_save()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(company, TenantSource.PublicSlug);
        var db = scope.ServiceProvider.GetRequiredService<VehicleRentalDbContext>();
        db.Vehicles.Add(new VehicleRental.Domain.Entities.Vehicle(Reg(), "Toyota", "Corolla", 2022, VehicleType.Car, 50m));

        // EF would send this one INSERT with no transaction at all (so no tenant setting); the guard refuses instead.
        DbUpdateException error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<TenantRequiredException>(error.InnerException);
    }

    // ---- real services under Row-Level Security ----------------------------------------------------------------

    [DatabaseFact]
    public async Task Services_see_and_create_only_their_own_companys_vehicles()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        string regA = Reg(), regB = Reg();

        await _db.RunAsTenantAsync(services, a, p => p.GetRequiredService<VehicleService>().AddVehicleAsync(Car(regA)));
        await _db.RunAsTenantAsync(services, b, p => p.GetRequiredService<VehicleService>().AddVehicleAsync(Car(regB)));

        IReadOnlyList<VehicleDto> seenByA = await _db.RunAsTenantAsync(services, a, p => p.GetRequiredService<VehicleService>().GetAllAsync());
        IReadOnlyList<VehicleDto> seenByB = await _db.RunAsTenantAsync(services, b, p => p.GetRequiredService<VehicleService>().GetAllAsync());

        Assert.Equal([regA], seenByA.Select(v => v.RegistrationNumber));
        Assert.Equal([regB], seenByB.Select(v => v.RegistrationNumber));
        Assert.Equal(1L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"CompanyId\" = @c", ("c", a)));

        // Looking up the other company's vehicle by its registration finds nothing, not "forbidden".
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsTenantAsync(services, a, p => p.GetRequiredService<VehicleService>().GetByRegistrationNumberAsync(regB)));
    }

    [DatabaseFact]
    public async Task Nothing_is_committed_when_the_handler_fails_and_a_failed_save_leaves_nothing_behind()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();
        string registration = Reg();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.RunAsTenantAsync<int>(services, company, async p =>
        {
            await p.GetRequiredService<VehicleService>().AddVehicleAsync(Car(registration));
            throw new InvalidOperationException("the handler failed after saving");
        }));

        Assert.Equal(0L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"RegistrationNumber\" = @r", ("r", registration)));
    }

    [DatabaseFact]
    public async Task The_unit_of_work_joins_the_tenant_transaction_and_commits_only_when_the_scope_does()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();
        string registration = Reg();

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(company, TenantSource.Membership);
            var transaction = scope.ServiceProvider.GetRequiredService<ITenantTransaction>();
            await transaction.BeginAsync();

            await scope.ServiceProvider.GetRequiredService<VehicleService>().AddVehicleAsync(Car(registration));

            // The unit of work saved, but did not commit: nobody else can see the row yet.
            Assert.Equal(0L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"RegistrationNumber\" = @r", ("r", registration)));
            Assert.True(transaction.IsActive);

            await transaction.CommitAsync();
        }

        Assert.Equal(1L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"RegistrationNumber\" = @r", ("r", registration)));
    }

    [DatabaseFact]
    public async Task A_scope_that_is_disposed_without_committing_rolls_back()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();
        string registration = Reg();

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(company, TenantSource.Membership);
            await scope.ServiceProvider.GetRequiredService<ITenantTransaction>().BeginAsync();
            await scope.ServiceProvider.GetRequiredService<VehicleService>().AddVehicleAsync(Car(registration));
        } // disposed: neither committed nor rolled back explicitly

        Assert.Equal(0L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"RegistrationNumber\" = @r", ("r", registration)));
    }

    [DatabaseFact]
    public async Task The_unit_of_work_refuses_to_lock_or_save_outside_the_tenant_transaction()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(company, TenantSource.Background);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<TenantRequiredException>(() => unitOfWork.LockVehicleAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<TenantRequiredException>(() => unitOfWork.SaveChangesAsync());
    }

    [DatabaseFact]
    public async Task A_duplicate_registration_is_the_same_409_conflict_inside_a_tenant_transaction()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();
        string registration = Reg();
        await _db.RunAsTenantAsync(services, company, p => p.GetRequiredService<VehicleService>().AddVehicleAsync(Car(registration)));

        ConflictException conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            _db.RunAsTenantAsync(services, company, p => p.GetRequiredService<VehicleService>().AddVehicleAsync(Car(registration))));

        // The service's own check answers first, exactly as it does without a tenant.
        Assert.Contains("already exists", conflict.Message, StringComparison.Ordinal);
    }
}
