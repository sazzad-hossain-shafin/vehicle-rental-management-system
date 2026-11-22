using Microsoft.Extensions.DependencyInjection;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The booking guarantees inside a tenant transaction: the vehicle row lock, the no-overlap exclusion constraint and the
/// one-active-rental rule, with the unit of work JOINING the tenant transaction, as the restricted runtime role, under
/// PREVIEW Row-Level Security (disposable database only).
/// </summary>
public class TenantBookingTests : IClassFixture<TenantTestDatabase>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private readonly TenantTestDatabase _db;

    public TenantBookingTests(TenantTestDatabase db) => _db = db;

    private async Task<Guid> NewVehicleAsync(ServiceProvider services, Guid company, string tag)
    {
        string registration = $"{tag}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        VehicleDto vehicle = await _db.RunAsTenantAsync(services, company, p =>
            p.GetRequiredService<VehicleService>().AddVehicleAsync(new AddVehicleRequest(registration, "Toyota", "Corolla", 2022, VehicleType.Car, 60m)));

        return vehicle.Id;
    }

    private Task<ReservationDto> ReserveAsync(ServiceProvider services, Guid company, Guid vehicle, Guid customer, int fromDays, int toDays) =>
        _db.RunAsTenantAsync(services, company, p => p.GetRequiredService<ReservationService>().CreateAsync(
            new CreateReservationRequest(customer, vehicle, Today.AddDays(fromDays), Today.AddDays(toDays))));

    [DatabaseFact]
    public async Task A_reservation_is_made_listed_and_cancelled_inside_the_tenant_transaction()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();
        Guid customer = await _db.AddCustomerAsync();
        Guid vehicle = await NewVehicleAsync(services, company, "KA");

        ReservationDto reservation = await ReserveAsync(services, company, vehicle, customer, 10, 13);

        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(1L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Reservations\" WHERE \"CompanyId\" = @c", ("c", company)));

        ReservationDto cancelled = await _db.RunAsTenantAsync(services, company, p => p.GetRequiredService<ReservationService>().CancelAsync(reservation.Id));
        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
    }

    [DatabaseFact]
    public async Task An_overlapping_reservation_is_the_same_409_conflict_and_a_back_to_back_one_is_allowed()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();
        Guid alice = await _db.AddCustomerAsync("A");
        Guid bob = await _db.AddCustomerAsync("B");
        Guid vehicle = await NewVehicleAsync(services, company, "KB");

        await ReserveAsync(services, company, vehicle, alice, 10, 14);

        ConflictException conflict = await Assert.ThrowsAsync<ConflictException>(() => ReserveAsync(services, company, vehicle, bob, 12, 16));
        Assert.Contains("not available", conflict.Message, StringComparison.Ordinal);

        await ReserveAsync(services, company, vehicle, bob, 14, 17); // half-open: starts the day the first one ends
    }

    [DatabaseFact]
    public async Task Two_simultaneous_reservations_of_one_vehicle_leave_exactly_one_winner_every_time()
    {
        await using ServiceProvider services = _db.BuildServices(_db.AppPool(maxPoolSize: 8));
        Guid company = await _db.NewCompanyAsync();
        Guid alice = await _db.AddCustomerAsync("A");
        Guid bob = await _db.AddCustomerAsync("B");

        for (int round = 0; round < 15; round++)
        {
            Guid vehicle = await NewVehicleAsync(services, company, "KC");

            Task<ReservationDto>[] attempts =
            [
                Task.Run(() => ReserveAsync(services, company, vehicle, alice, 20, 24)),
                Task.Run(() => ReserveAsync(services, company, vehicle, bob, 22, 26)),
            ];

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
            Assert.Equal(1L, await _db.OwnerCountAsync("SELECT count(*) FROM \"Reservations\" WHERE \"VehicleId\" = @v AND \"Status\" = 'Active'", ("v", vehicle)));
        }
    }

    [DatabaseFact]
    public async Task A_walk_in_rental_and_a_reservation_for_the_same_days_cannot_both_win()
    {
        // The cross-table race that the vehicle row lock exists for (ADR 005), now inside the tenant transaction.
        await using ServiceProvider services = _db.BuildServices(_db.AppPool(maxPoolSize: 8));
        Guid company = await _db.NewCompanyAsync();
        Guid alice = await _db.AddCustomerAsync("A");
        Guid bob = await _db.AddCustomerAsync("B");

        for (int round = 0; round < 15; round++)
        {
            Guid vehicle = await NewVehicleAsync(services, company, "KD");

            Task<object> rental = Task.Run(async () => (object)await _db.RunAsTenantAsync(services, company, p =>
                p.GetRequiredService<RentalService>().StartRentalAsync(new StartRentalByIdRequest(vehicle, alice, 3))));
            Task<object> reservation = Task.Run(async () => (object)await ReserveAsync(services, company, vehicle, bob, 0, 3));

            var attempts = new[] { rental, reservation };

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
    public async Task Pickup_and_return_work_and_a_second_active_rental_of_the_vehicle_is_refused()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid company = await _db.NewCompanyAsync();
        Guid alice = await _db.AddCustomerAsync("A");
        Guid bob = await _db.AddCustomerAsync("B");
        Guid vehicle = await NewVehicleAsync(services, company, "KE");

        ReservationDto reservation = await ReserveAsync(services, company, vehicle, alice, 0, 3);
        PickupResultDto pickup = await _db.RunAsTenantAsync(services, company, p => p.GetRequiredService<ReservationService>().PickUpAsync(reservation.Id));
        Assert.Equal(ReservationStatus.Fulfilled, pickup.Reservation.Status);

        await Assert.ThrowsAsync<ConflictException>(() => _db.RunAsTenantAsync(services, company, p =>
            p.GetRequiredService<RentalService>().StartRentalAsync(new StartRentalByIdRequest(vehicle, bob, 2))));

        RentalDto returned = await _db.RunAsTenantAsync(services, company, p => p.GetRequiredService<RentalService>().ReturnRentalAsync(pickup.Rental.Id));
        Assert.Equal(RentalStatus.Completed, returned.Status);
    }

    [DatabaseFact]
    public async Task One_company_cannot_see_reserve_cancel_or_return_another_companys_vehicles_reservations_or_rentals()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        Guid customer = await _db.AddCustomerAsync("X");
        Guid vehicleA = await NewVehicleAsync(services, a, "KF");
        ReservationDto reservationA = await ReserveAsync(services, a, vehicleA, customer, 0, 3);
        PickupResultDto pickupA = await _db.RunAsTenantAsync(services, a, p => p.GetRequiredService<ReservationService>().PickUpAsync(reservationA.Id));

        // B cannot reserve, quote or look up A's vehicle: it does not exist as far as B can tell.
        await Assert.ThrowsAsync<NotFoundException>(() => ReserveAsync(services, b, vehicleA, customer, 10, 12));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsTenantAsync(services, b, p =>
            p.GetRequiredService<ReservationService>().GetQuoteAsync(vehicleA, Today.AddDays(10), Today.AddDays(12))));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsTenantAsync(services, b, p => p.GetRequiredService<ReservationService>().GetAsync(reservationA.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsTenantAsync(services, b, p => p.GetRequiredService<ReservationService>().CancelAsync(reservationA.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsTenantAsync(services, b, p => p.GetRequiredService<RentalService>().ReturnRentalAsync(pickupA.Rental.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsTenantAsync(services, b, p => p.GetRequiredService<RentalService>().GetRentalAsync(pickupA.Rental.Id)));

        // And A's data is exactly as A left it.
        Assert.Equal(RentalStatus.Active, (await _db.RunAsTenantAsync(services, a, p => p.GetRequiredService<RentalService>().GetRentalAsync(pickupA.Rental.Id))).Status);
    }

    [DatabaseFact]
    public async Task Availability_search_and_paging_only_ever_cover_the_callers_company()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        for (int i = 0; i < 4; i++)
        {
            await NewVehicleAsync(services, a, "LA");
        }

        for (int i = 0; i < 7; i++)
        {
            await NewVehicleAsync(services, b, "LB");
        }

        var forA = await _db.RunAsTenantAsync(services, a, p => p.GetRequiredService<ReservationService>().GetAvailableVehiclesPageAsync(Today.AddDays(5), Today.AddDays(8), pageSize: 3));

        Assert.Equal(4, forA.TotalCount); // the total is the company's, not the platform's
        Assert.Equal(3, forA.Items.Count);
        Assert.All(forA.Items, v => Assert.StartsWith("LA-", v.RegistrationNumber, StringComparison.Ordinal));
    }

    [DatabaseFact]
    public async Task A_background_job_visits_each_active_company_in_turn_and_sees_only_that_company()
    {
        await using ServiceProvider services = _db.BuildServices();
        Guid a = await _db.NewCompanyAsync();
        Guid b = await _db.NewCompanyAsync();
        Guid pending = await _db.NewCompanyAsync(status: "PendingReview");
        await NewVehicleAsync(services, a, "MA");
        await NewVehicleAsync(services, a, "MA");
        await NewVehicleAsync(services, b, "MB");

        var seen = new Dictionary<Guid, int>();

        await services.GetRequiredService<VehicleRental.Application.Tenancy.ITenantScopeRunner>().ForEachActiveCompanyAsync(async (company, p, token) =>
        {
            int count = (await p.GetRequiredService<VehicleService>().GetAllAsync(token)).Count;

            lock (seen)
            {
                seen[company] = count;
            }
        });

        Assert.Equal(2, seen[a]);
        Assert.Equal(1, seen[b]);
        Assert.False(seen.ContainsKey(pending)); // pending companies are never visited
    }
}
