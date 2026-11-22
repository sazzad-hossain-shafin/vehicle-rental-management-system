using Microsoft.Extensions.DependencyInjection;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Application.Tenancy;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// Tenant mode is OFF (the default) and the database has no Row-Level Security: this is exactly how the application runs
/// today, except that it connects as the restricted runtime role. Everything must behave as before, on the ORIGINAL
/// unit-of-work transaction path (it begins, commits and discards its own transaction).
/// </summary>
public class TenancyDisabledCompatibilityTests : IClassFixture<TenancyDisabledCompatibilityTests.NoPreviewDatabase>
{
    public sealed class NoPreviewDatabase : TenantTestDatabase
    {
        public NoPreviewDatabase() => PreviewRls = false;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private readonly NoPreviewDatabase _db;

    public TenancyDisabledCompatibilityTests(NoPreviewDatabase db) => _db = db;

    private ServiceProvider Services(int pool = 8) => _db.BuildServices(_db.AppPool(pool), tenancyEnabled: false);

    private static async Task<T> InScopeAsync<T>(ServiceProvider services, Func<IServiceProvider, Task<T>> work)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await work(scope.ServiceProvider);
    }

    private static Task<VehicleDto> NewVehicleAsync(ServiceProvider services, string tag) =>
        InScopeAsync(services, p => p.GetRequiredService<VehicleService>().AddVehicleAsync(
            new AddVehicleRequest($"{tag}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", "Toyota", "Corolla", 2022, VehicleType.Car, 60m)));

    [DatabaseFact]
    public async Task No_tenant_is_bound_and_binding_one_is_refused_while_tenancy_is_disabled()
    {
        await using ServiceProvider services = Services();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<ITenantContext>().IsBound);
        Assert.Throws<TenancyDisabledException>(() => scope.ServiceProvider.GetRequiredService<ITenantBinder>().Bind(Guid.NewGuid(), TenantSource.Background));
        await Assert.ThrowsAsync<TenancyDisabledException>(() => services.GetRequiredService<ITenantScopeRunner>().RunAsync(Guid.NewGuid(), (_, _) => Task.FromResult(1)));
    }

    [DatabaseFact]
    public async Task The_whole_booking_flow_works_as_the_restricted_role_on_the_original_path()
    {
        await using ServiceProvider services = Services();
        Guid alice = await _db.AddCustomerAsync("A");
        Guid bob = await _db.AddCustomerAsync("B");
        VehicleDto vehicle = await NewVehicleAsync(services, "NA");

        ReservationDto reservation = await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(
            new CreateReservationRequest(alice, vehicle.Id, Today, Today.AddDays(3))));

        // The exclusion constraint / availability check refuse an overlap with the same 409.
        await Assert.ThrowsAsync<ConflictException>(() => InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(
            new CreateReservationRequest(bob, vehicle.Id, Today.AddDays(1), Today.AddDays(4)))));

        PickupResultDto pickup = await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().PickUpAsync(reservation.Id));
        Assert.Equal(ReservationStatus.Fulfilled, pickup.Reservation.Status);

        // One active rental per vehicle.
        await Assert.ThrowsAsync<ConflictException>(() => InScopeAsync(services, p => p.GetRequiredService<RentalService>().StartRentalAsync(
            new StartRentalByIdRequest(vehicle.Id, bob, 2))));

        RentalDto returned = await InScopeAsync(services, p => p.GetRequiredService<RentalService>().ReturnRentalAsync(pickup.Rental.Id));
        Assert.Equal(RentalStatus.Completed, returned.Status);

        // Cancellation of a future reservation.
        ReservationDto future = await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(
            new CreateReservationRequest(bob, vehicle.Id, Today.AddDays(10), Today.AddDays(12))));
        Assert.Equal(ReservationStatus.Cancelled, (await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CancelAsync(future.Id))).Status);
    }

    [DatabaseFact]
    public async Task The_vehicle_row_lock_still_leaves_exactly_one_winner_in_both_races_as_the_restricted_role()
    {
        await using ServiceProvider services = Services();
        Guid alice = await _db.AddCustomerAsync("A");
        Guid bob = await _db.AddCustomerAsync("B");

        for (int round = 0; round < 15; round++)
        {
            VehicleDto vehicle = await NewVehicleAsync(services, "NB");
            bool reservationVsReservation = round % 2 == 0;

            Task<object> first = Task.Run(async () => (object)await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(
                new CreateReservationRequest(alice, vehicle.Id, Today.AddDays(20), Today.AddDays(24)))));
            Task<object> second = reservationVsReservation
                ? Task.Run(async () => (object)await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(
                    new CreateReservationRequest(bob, vehicle.Id, Today.AddDays(22), Today.AddDays(26)))))
                : Task.Run(async () => (object)await InScopeAsync(services, p => p.GetRequiredService<RentalService>().StartRentalAsync(
                    new StartRentalByIdRequest(vehicle.Id, bob, 40))));

            var attempts = new[] { first, second };

            await Task.WhenAll(attempts.Select(async t =>
            {
                try
                {
                    await t;
                }
                catch (ConflictException)
                {
                }
            }));

            Assert.Equal(1, attempts.Count(t => t.IsCompletedSuccessfully));
            Assert.Equal(1, attempts.Count(t => t.IsFaulted && t.Exception!.InnerException is ConflictException));
        }
    }

    [DatabaseFact]
    public async Task A_database_conflict_discards_the_unit_of_works_own_transaction_exactly_as_before()
    {
        await using ServiceProvider services = Services(pool: 1);
        Guid alice = await _db.AddCustomerAsync("A");
        VehicleDto vehicle = await NewVehicleAsync(services, "NC");
        await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(new CreateReservationRequest(alice, vehicle.Id, Today.AddDays(5), Today.AddDays(8))));

        // A conflict ends that scope's transaction; the single pooled connection is immediately usable again.
        await Assert.ThrowsAsync<ConflictException>(() => InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(
            new CreateReservationRequest(alice, vehicle.Id, Today.AddDays(6), Today.AddDays(9)))));

        ReservationDto next = await InScopeAsync(services, p => p.GetRequiredService<ReservationService>().CreateAsync(
            new CreateReservationRequest(alice, vehicle.Id, Today.AddDays(30), Today.AddDays(32))));
        Assert.Equal(ReservationStatus.Active, next.Status);
    }

    [DatabaseFact]
    public async Task Registration_and_account_style_work_that_shares_the_context_still_runs_as_the_restricted_role()
    {
        await using ServiceProvider services = Services();
        string email = $"role-{Guid.NewGuid():N}@example.test";

        VehicleRental.Application.Accounts.UserDto user = await InScopeAsync(services, p => p.GetRequiredService<VehicleRental.Application.Accounts.IAccountService>()
            .RegisterCustomerAsync(email, "Aa1-" + Guid.NewGuid().ToString("N"), "Role Test"));

        Assert.NotNull(user.CustomerId);
        Assert.Contains(VehicleRental.Application.Security.Roles.Customer, user.Roles);

        VehicleRental.Application.Accounts.LoginResult? login = await InScopeAsync(services, p => p.GetRequiredService<VehicleRental.Application.Accounts.IAccountService>()
            .LoginAsync(email, "wrong-password-1A"));
        Assert.Null(login);

        Assert.True(await InScopeAsync(services, async p => await p.GetRequiredService<VehicleRentalDbContext>().Database.CanConnectAsync()));
    }
}
