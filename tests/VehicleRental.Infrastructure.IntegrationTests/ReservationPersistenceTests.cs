using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Reservations;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// Reservations on a real PostgreSQL database: the mapping, the constraints that back up the domain rules, and above
/// all the guarantee that two active reservations of one vehicle can never overlap, however the requests arrive.
/// </summary>
public class ReservationPersistenceTests : DatabaseTestBase
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public ReservationPersistenceTests(PostgresFixture database) : base(database)
    {
    }

    private static Reservation NewReservation(
        Vehicle vehicle,
        Customer customer,
        int startOffset,
        int endOffset,
        DateOnly? today = null) =>
        Reservation.Create(
            customer,
            vehicle,
            Today.AddDays(startOffset),
            Today.AddDays(endOffset),
            today ?? Today,
            Now,
            new NormalPricingStrategy());

    private async Task<(Guid VehicleId, Guid AliceId, Guid BobId)> SeedAsync(string registration = "ABC-123")
    {
        Vehicle vehicle = TestEntities.NewVehicle(registration);
        Customer alice = TestEntities.NewCustomer("C1", "Alice");
        Customer bob = TestEntities.NewCustomer("C2", "Bob");

        await using var session = Database.CreateSession();
        await session.Vehicles.AddAsync(vehicle);
        await session.Customers.AddAsync(alice);
        await session.Customers.AddAsync(bob);
        await session.UnitOfWork.SaveChangesAsync();

        return (vehicle.Id, alice.Id, bob.Id);
    }

    /// <summary>Saves a reservation in its own session, loading the vehicle and customer fresh.</summary>
    private async Task<Guid> ReserveAsync(Guid vehicleId, Guid customerId, int startOffset, int endOffset)
    {
        await using var session = Database.CreateSession();
        var vehicle = (await session.Vehicles.GetByIdAsync(vehicleId))!;
        var customer = (await session.Customers.GetByIdAsync(customerId))!;
        Reservation reservation = NewReservation(vehicle, customer, startOffset, endOffset);

        await session.Reservations.AddAsync(reservation);
        await session.UnitOfWork.SaveChangesAsync();

        return reservation.Id;
    }

    // ----- Mapping -----

    [DatabaseFact]
    public async Task AReservation_RoundTripsWithItsQuote()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        Guid id = await ReserveAsync(vehicleId, aliceId, 5, 8);

        await using var session = Database.CreateSession();
        Reservation loaded = (await session.Reservations.GetByIdAsync(id))!;

        Assert.Equal(ReservationStatus.Active, loaded.Status);
        Assert.Equal(Today.AddDays(5), loaded.StartDate);
        Assert.Equal(Today.AddDays(8), loaded.EndDate);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(3, loaded.BillableDays);
        Assert.Equal(100m, loaded.DailyRateAtReservation);
        Assert.Equal(300m, loaded.TotalCost);
        Assert.Equal("Normal pricing", loaded.PricingDescription);
        Assert.Equal("Alice", loaded.Customer.Name);
        Assert.Equal("ABC-123", loaded.Vehicle.RegistrationNumber);
        Assert.Null(loaded.RentalId);
    }

    [DatabaseFact]
    public async Task ThePricingSnapshot_DoesNotChangeWhenTheVehicleRateIsChangedLater()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        Guid id = await ReserveAsync(vehicleId, aliceId, 5, 8);

        await using (var context = Database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Vehicles\" SET \"DailyRate\" = 999");
        }

        await using var session = Database.CreateSession();
        Reservation loaded = (await session.Reservations.GetByIdAsync(id))!;

        Assert.Equal(999m, loaded.Vehicle.DailyRate);
        Assert.Equal(100m, loaded.DailyRateAtReservation);
        Assert.Equal(300m, loaded.TotalCost);
    }

    // ----- The database backstop against double booking -----

    [DatabaseFact]
    public async Task TheExclusionConstraintAndItsExtension_ExistInTheMigratedDatabase()
    {
        await using var context = Database.CreateContext();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var constraint = new NpgsqlCommand(
            "SELECT contype::text FROM pg_constraint WHERE conname = @name", connection);
        constraint.Parameters.AddWithValue("name", "EX_Reservations_NoOverlappingActive");
        Assert.Equal("x", (string?)await constraint.ExecuteScalarAsync());   // x = exclusion constraint

        await using var extension = new NpgsqlCommand(
            "SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'", connection);
        Assert.Equal(1L, await extension.ExecuteScalarAsync());
    }

    [DatabaseTheory]
    [InlineData(5, 8)]    // identical
    [InlineData(6, 7)]    // inside
    [InlineData(4, 6)]    // overlaps the start
    [InlineData(7, 10)]   // overlaps the end
    [InlineData(3, 12)]   // contains it
    public async Task OverlappingActiveReservations_OfOneVehicle_AreRefusedByTheDatabase(int start, int end)
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();
        await ReserveAsync(vehicleId, aliceId, 5, 8);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ReserveAsync(vehicleId, bobId, start, end));

        Assert.Contains("already reserved", ex.Message);
        Assert.DoesNotContain("EX_Reservations", ex.Message);   // no database internals reach the caller

        await using var context = Database.CreateContext();
        Assert.Equal(1, await context.Reservations.CountAsync());
    }

    [DatabaseFact]
    public async Task AdjacentReservations_AreAllowed_InEitherOrder()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();
        await ReserveAsync(vehicleId, aliceId, 5, 8);

        await ReserveAsync(vehicleId, bobId, 8, 10);   // starts the day the first one ends
        await ReserveAsync(vehicleId, bobId, 3, 5);    // ends the day the first one starts

        await using var context = Database.CreateContext();
        Assert.Equal(3, await context.Reservations.CountAsync());
    }

    [DatabaseFact]
    public async Task TheSameDates_OnAnotherVehicle_AreAllowed()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();
        await using (var session = Database.CreateSession())
        {
            await session.Vehicles.AddAsync(TestEntities.NewVehicle("XYZ-999"));
            await session.UnitOfWork.SaveChangesAsync();
        }

        Guid otherId;
        await using (var session = Database.CreateSession())
        {
            otherId = (await session.Vehicles.GetByRegistrationNumberAsync("XYZ-999"))!.Id;
        }

        await ReserveAsync(vehicleId, aliceId, 5, 8);
        await ReserveAsync(otherId, bobId, 5, 8);

        await using var context = Database.CreateContext();
        Assert.Equal(2, await context.Reservations.CountAsync());
    }

    [DatabaseFact]
    public async Task ACancelledReservation_ReleasesTheDates()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();
        Guid first = await ReserveAsync(vehicleId, aliceId, 5, 8);

        await using (var session = Database.CreateSession())
        {
            var reservation = (await session.Reservations.GetByIdAsync(first))!;
            reservation.Cancel(Now);
            await session.UnitOfWork.SaveChangesAsync();
        }

        await ReserveAsync(vehicleId, bobId, 5, 8);

        await using var context = Database.CreateContext();
        Assert.Equal(1, await context.Reservations.CountAsync(r => r.Status == ReservationStatus.Active));
        Assert.Equal(1, await context.Reservations.CountAsync(r => r.Status == ReservationStatus.Cancelled));
    }

    [DatabaseFact]
    public async Task TheConstraint_AppliesEvenToSqlThatBypassesTheApplication()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        await ReserveAsync(vehicleId, aliceId, 5, 8);

        await using var context = Database.CreateContext();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Reservations" ("Id","CustomerId","VehicleId","StartDate","EndDate","Status","CreatedAt",
                "DailyRateAtReservation","BillableDays","PricingDescription","TotalCost")
            SELECT gen_random_uuid(), "CustomerId", "VehicleId", "StartDate" + 1, "EndDate" + 1, 'Active', now(),
                100, 3, 'Normal pricing', 300 FROM "Reservations"
            """));

        Assert.Equal("23P01", ex.SqlState);   // exclusion_violation
    }

    // ----- Races between two requests -----

    [DatabaseFact]
    public async Task TwoRequestsThatBothCheckedAndSawTheVehicleFree_OnlyTheFirstToSaveWins()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();

        await using var first = Database.CreateSession();
        await using var second = Database.CreateSession();

        var firstReservation = NewReservation(
            (await first.Vehicles.GetByIdAsync(vehicleId))!, (await first.Customers.GetByIdAsync(aliceId))!, 5, 8);
        var secondReservation = NewReservation(
            (await second.Vehicles.GetByIdAsync(vehicleId))!, (await second.Customers.GetByIdAsync(bobId))!, 6, 9);

        await first.Reservations.AddAsync(firstReservation);
        await second.Reservations.AddAsync(secondReservation);

        await first.UnitOfWork.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.UnitOfWork.SaveChangesAsync());

        await using var context = Database.CreateContext();
        Assert.Equal(firstReservation.Id, (await context.Reservations.SingleAsync()).Id);
    }

    [DatabaseFact]
    public async Task ManyConcurrentRequestsForOverlappingDates_ExactlyOneSucceeds()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Database.ResetAsync();
            var (vehicleId, aliceId, bobId) = await SeedAsync();
            using var start = new ManualResetEventSlim(false);

            Task<Exception?> Reserve(Guid customerId, int startOffset, int endOffset) => Task.Run(async () =>
            {
                try
                {
                    await using var session = Database.CreateSession();
                    var service = new ReservationService(
                        session.Vehicles, session.Customers, session.Reservations,
                        session.Rentals, session.Availability, session.UnitOfWork);

                    start.Wait();
                    await service.CreateAsync(new CreateReservationRequest(
                        customerId, vehicleId, today.AddDays(startOffset), today.AddDays(endOffset)));

                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var tasks = new[] { Reserve(aliceId, 5, 8), Reserve(bobId, 6, 9), Reserve(aliceId, 7, 10), Reserve(bobId, 4, 8) };
            start.Set();
            Exception?[] results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.IsType<ConflictException>(r));

            await using var context = Database.CreateContext();
            Assert.Equal(1, await context.Reservations.CountAsync());
        }
    }

    // ----- Pickup: one rental, atomically -----

    [DatabaseFact]
    public async Task APickup_SavesTheRentalTheVehicleStatusAndTheReservationTogether()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        Guid id = await ReserveAsync(vehicleId, aliceId, 0, 3);

        await using (var session = Database.CreateSession())
        {
            var reservation = (await session.Reservations.GetByIdAsync(id))!;
            Rental rental = reservation.PickUp(Today, Now);
            await session.Rentals.AddAsync(rental);
            await session.UnitOfWork.SaveChangesAsync();
        }

        await using var context = Database.CreateContext();
        var saved = await context.Reservations.SingleAsync();
        var rentalRow = await context.Rentals.SingleAsync();

        Assert.Equal(ReservationStatus.Fulfilled, saved.Status);
        Assert.Equal(rentalRow.Id, saved.RentalId);
        Assert.Equal(300m, rentalRow.TotalCost);
        Assert.Equal(VehicleAvailabilityStatus.Rented, (await context.Vehicles.SingleAsync()).AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task TwoPickupsOfTheSameReservation_OnlyOneRentalIsCreated()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        Guid id = await ReserveAsync(vehicleId, aliceId, 0, 3);

        await using var first = Database.CreateSession();
        await using var second = Database.CreateSession();
        var seenByFirst = (await first.Reservations.GetByIdAsync(id))!;
        var seenBySecond = (await second.Reservations.GetByIdAsync(id))!;

        Rental firstRental = seenByFirst.PickUp(Today, Now);
        Rental secondRental = seenBySecond.PickUp(Today, Now);
        await first.Rentals.AddAsync(firstRental);
        await second.Rentals.AddAsync(secondRental);

        await first.UnitOfWork.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.UnitOfWork.SaveChangesAsync());

        await using var context = Database.CreateContext();
        Assert.Equal(firstRental.Id, (await context.Rentals.SingleAsync()).Id);
        Assert.Equal(firstRental.Id, (await context.Reservations.SingleAsync()).RentalId);
    }

    [DatabaseFact]
    public async Task ConcurrentPickups_ThroughTheService_ExactlyOneSucceeds()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Database.ResetAsync();
            var (vehicleId, aliceId, _) = await SeedAsync();

            Guid id;
            await using (var session = Database.CreateSession())
            {
                var service = new ReservationService(
                    session.Vehicles, session.Customers, session.Reservations,
                    session.Rentals, session.Availability, session.UnitOfWork);
                id = (await service.CreateAsync(new CreateReservationRequest(
                    aliceId, vehicleId, today, today.AddDays(3)))).Id;
            }

            using var start = new ManualResetEventSlim(false);

            Task<Exception?> PickUp() => Task.Run(async () =>
            {
                try
                {
                    await using var session = Database.CreateSession();
                    var service = new ReservationService(
                        session.Vehicles, session.Customers, session.Reservations,
                        session.Rentals, session.Availability, session.UnitOfWork);

                    start.Wait();
                    await service.PickUpAsync(id);

                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var tasks = new[] { PickUp(), PickUp(), PickUp() };
            start.Set();
            Exception?[] results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.IsType<ConflictException>(r));

            await using var context = Database.CreateContext();
            Assert.Equal(1, await context.Rentals.CountAsync());
            Assert.Equal(ReservationStatus.Fulfilled, (await context.Reservations.SingleAsync()).Status);
        }
    }

    [DatabaseFact]
    public async Task APickup_ThatFailsWhileSaving_RollsBackEverything()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();
        Guid id = await ReserveAsync(vehicleId, aliceId, 0, 3);

        await using var pickup = Database.CreateSession();
        var reservation = (await pickup.Reservations.GetByIdAsync(id))!;   // sees the vehicle available
        Rental rental = reservation.PickUp(Today, Now);

        // Meanwhile a walk-in rental takes the vehicle.
        await using (var walkIn = Database.CreateSession())
        {
            var vehicle = (await walkIn.Vehicles.GetByIdAsync(vehicleId))!;
            var bob = (await walkIn.Customers.GetByIdAsync(bobId))!;
            await walkIn.Rentals.AddAsync(Rental.Start(bob, vehicle, Today, Today.AddDays(1), new NormalPricingStrategy()));
            await walkIn.UnitOfWork.SaveChangesAsync();
        }

        await pickup.Rentals.AddAsync(rental);
        await Assert.ThrowsAsync<ConflictException>(() => pickup.UnitOfWork.SaveChangesAsync());

        await using var context = Database.CreateContext();
        var saved = await context.Reservations.SingleAsync();

        Assert.Equal(ReservationStatus.Active, saved.Status);   // not half-fulfilled
        Assert.Null(saved.RentalId);
        Assert.Equal("Bob", (await context.Rentals.Include(r => r.Customer).SingleAsync()).Customer.Name);
    }

    // ----- Integrity -----

    [DatabaseFact]
    public async Task TheDatabaseRefusesAReservationThatBreaksTheRules()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();

        await using var context = Database.CreateContext();

        // Parameters, not string building: the values are passed to PostgreSQL separately from the SQL text.
        async Task<string?> InsertAsync(int startOffset, int endOffset, string status) =>
            (await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Reservations" ("Id","CustomerId","VehicleId","StartDate","EndDate","Status","CreatedAt",
                    "DailyRateAtReservation","BillableDays","PricingDescription","TotalCost")
                VALUES (gen_random_uuid(), {0}, {1}, current_date + {2}, current_date + {3},
                    {4}, now(), 100, 1, 'Normal pricing', 100)
                """,
                aliceId, vehicleId, startOffset, endOffset, status))).SqlState;

        Assert.Equal("23514", await InsertAsync(5, 5, "Active"));      // ends the day it starts
        Assert.Equal("23514", await InsertAsync(5, 3, "Active"));      // ends before it starts
        Assert.Equal("23514", await InsertAsync(5, 6, "Fulfilled"));   // fulfilled but no rental
        Assert.Equal("23514", await InsertAsync(5, 6, "Cancelled"));   // cancelled but no timestamp
    }

    [DatabaseFact]
    public async Task AVehicleOrCustomerWithReservations_CannotBeDeleted()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        await ReserveAsync(vehicleId, aliceId, 5, 8);

        await using var context = Database.CreateContext();

        var vehicleDelete = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM \"Vehicles\" WHERE \"Id\" = {0}", vehicleId));
        var customerDelete = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM \"Customers\" WHERE \"Id\" = {0}", aliceId));

        Assert.Equal("23503", vehicleDelete.SqlState);
        Assert.Equal("23503", customerDelete.SqlState);
    }

    // ----- Queries -----

    [DatabaseFact]
    public async Task TheCustomerPage_ContainsOnlyThatCustomersReservations_InStartOrder_WithRealPaging()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();
        await using (var session = Database.CreateSession())
        {
            for (int i = 1; i <= 2; i++)
            {
                await session.Vehicles.AddAsync(TestEntities.NewVehicle($"EXTRA-{i}"));
            }

            await session.UnitOfWork.SaveChangesAsync();
        }

        List<Guid> extra;
        await using (var session = Database.CreateSession())
        {
            extra = [(await session.Vehicles.GetByRegistrationNumberAsync("EXTRA-1"))!.Id,
                     (await session.Vehicles.GetByRegistrationNumberAsync("EXTRA-2"))!.Id];
        }

        Guid a3 = await ReserveAsync(vehicleId, aliceId, 20, 22);
        Guid a1 = await ReserveAsync(vehicleId, aliceId, 2, 4);
        Guid a2 = await ReserveAsync(extra[0], aliceId, 10, 12);
        await ReserveAsync(extra[1], bobId, 1, 3);

        await using var session2 = Database.CreateSession();
        var firstPage = await session2.Reservations.GetPageForCustomerAsync(aliceId, 0, 2);
        var secondPage = await session2.Reservations.GetPageForCustomerAsync(aliceId, 2, 2);
        var bobs = await session2.Reservations.GetPageForCustomerAsync(bobId, 0, 10);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(new[] { a1, a2 }, firstPage.Items.Select(r => r.Id));
        Assert.Equal(new[] { a3 }, secondPage.Items.Select(r => r.Id));
        Assert.Equal(1, bobs.TotalCount);
        Assert.All(firstPage.Items.Concat(secondPage.Items), r => Assert.Equal("Alice", r.Customer.Name));
    }

    [DatabaseFact]
    public async Task TheStaffPage_FiltersByStatus_AndOrdersDeterministically()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();
        Guid a = await ReserveAsync(vehicleId, aliceId, 5, 6);
        Guid b = await ReserveAsync(vehicleId, bobId, 5 + 1, 7);
        Guid c = await ReserveAsync(vehicleId, aliceId, 8, 9);

        await using (var session = Database.CreateSession())
        {
            (await session.Reservations.GetByIdAsync(b))!.Cancel(Now);
            await session.UnitOfWork.SaveChangesAsync();
        }

        await using var read = Database.CreateSession();
        var all = await read.Reservations.GetPageAsync(null, 0, 10);
        var active = await read.Reservations.GetPageAsync(ReservationStatus.Active, 0, 10);
        var cancelled = await read.Reservations.GetPageAsync(ReservationStatus.Cancelled, 0, 10);

        Assert.Equal(new[] { a, b, c }, all.Items.Select(r => r.Id));
        Assert.Equal(new[] { a, c }, active.Items.Select(r => r.Id));
        Assert.Equal(2, active.TotalCount);
        Assert.Equal(new[] { b }, cancelled.Items.Select(r => r.Id));
    }

    [DatabaseFact]
    public async Task HasActiveOverlap_UsesTheHalfOpenInterval_AndIgnoresCancelled()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        Guid id = await ReserveAsync(vehicleId, aliceId, 5, 8);

        await using var session = Database.CreateSession();

        Assert.True(await session.Reservations.HasActiveOverlapAsync(vehicleId, Today.AddDays(7), Today.AddDays(9)));
        Assert.False(await session.Reservations.HasActiveOverlapAsync(vehicleId, Today.AddDays(8), Today.AddDays(9)));
        Assert.False(await session.Reservations.HasActiveOverlapAsync(vehicleId, Today.AddDays(3), Today.AddDays(5)));
        Assert.False(await session.Reservations.HasActiveOverlapAsync(Guid.NewGuid(), Today.AddDays(5), Today.AddDays(8)));

        (await session.Reservations.GetByIdAsync(id))!.Cancel(Now);
        await session.UnitOfWork.SaveChangesAsync();

        Assert.False(await session.Reservations.HasActiveOverlapAsync(vehicleId, Today.AddDays(5), Today.AddDays(8)));
    }

    // ----- Availability -----

    [DatabaseFact]
    public async Task Availability_ExcludesReservedAndRentedVehicles_ForTheRequestedDates()
    {
        var (reservedId, aliceId, bobId) = await SeedAsync("AAA-111");
        await using (var session = Database.CreateSession())
        {
            await session.Vehicles.AddAsync(TestEntities.NewVehicle("BBB-222", type: VehicleType.Van));
            await session.Vehicles.AddAsync(TestEntities.NewVehicle("CCC-333", dailyRate: 40m));
            await session.Vehicles.AddAsync(TestEntities.NewVehicle("DDD-444"));
            await session.UnitOfWork.SaveChangesAsync();
        }

        Guid van, cheap, rented;
        await using (var session = Database.CreateSession())
        {
            van = (await session.Vehicles.GetByRegistrationNumberAsync("BBB-222"))!.Id;
            cheap = (await session.Vehicles.GetByRegistrationNumberAsync("CCC-333"))!.Id;
            rented = (await session.Vehicles.GetByRegistrationNumberAsync("DDD-444"))!.Id;
        }

        await ReserveAsync(reservedId, aliceId, 5, 8);   // reserved Oct 6 to Oct 9

        await using (var session = Database.CreateSession())
        {
            var vehicle = (await session.Vehicles.GetByIdAsync(rented))!;
            var bob = (await session.Customers.GetByIdAsync(bobId))!;
            await session.Rentals.AddAsync(Rental.Start(bob, vehicle, Today, Today.AddDays(6), new NormalPricingStrategy()));
            await session.UnitOfWork.SaveChangesAsync();   // rented Oct 1 to Oct 7
        }

        await using var read = Database.CreateSession();

        var during = await read.Availability.SearchAvailableAsync(null, null, Today.AddDays(5), Today.AddDays(7), Today, 0, 10);
        var afterBoth = await read.Availability.SearchAvailableAsync(null, null, Today.AddDays(8), Today.AddDays(10), Today, 0, 10);
        var vans = await read.Availability.SearchAvailableAsync(VehicleType.Van, null, Today.AddDays(5), Today.AddDays(7), Today, 0, 10);
        var inexpensive = await read.Availability.SearchAvailableAsync(null, 50m, Today.AddDays(5), Today.AddDays(7), Today, 0, 10);
        var paged = await read.Availability.SearchAvailableAsync(null, null, Today.AddDays(8), Today.AddDays(10), Today, 1, 2);

        Assert.Equal(new[] { van, cheap }, during.Items.Select(v => v.Id));          // ordered by registration number
        Assert.Equal(2, during.TotalCount);
        Assert.Equal(4, afterBoth.TotalCount);                                       // the reservation ended Oct 9; the rental Oct 7
        Assert.Equal(new[] { van }, vans.Items.Select(v => v.Id));
        Assert.Equal(new[] { cheap }, inexpensive.Items.Select(v => v.Id));
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(4, paged.TotalCount);

        Assert.False(await read.Availability.IsAvailableAsync(reservedId, Today.AddDays(7), Today.AddDays(9), Today));
        Assert.True(await read.Availability.IsAvailableAsync(reservedId, Today.AddDays(8), Today.AddDays(9), Today));
        Assert.False(await read.Availability.IsAvailableAsync(rented, Today.AddDays(5), Today.AddDays(8), Today));
        Assert.True(await read.Availability.IsAvailableAsync(rented, Today.AddDays(6), Today.AddDays(8), Today));
    }

    [DatabaseFact]
    public async Task AnOverdueRental_StillOccupiesTheVehicleToday()
    {
        var (vehicleId, aliceId, _) = await SeedAsync();
        await using (var session = Database.CreateSession())
        {
            var vehicle = (await session.Vehicles.GetByIdAsync(vehicleId))!;
            var alice = (await session.Customers.GetByIdAsync(aliceId))!;
            await session.Rentals.AddAsync(Rental.Start(alice, vehicle, Today, Today.AddDays(2), new NormalPricingStrategy()));
            await session.UnitOfWork.SaveChangesAsync();   // due back Oct 3
        }

        DateOnly overdueToday = Today.AddDays(10);

        await using var read = Database.CreateSession();

        Assert.False(await read.Availability.IsAvailableAsync(vehicleId, overdueToday, overdueToday.AddDays(2), overdueToday));
        Assert.True(await read.Availability.IsAvailableAsync(vehicleId, overdueToday.AddDays(1), overdueToday.AddDays(3), overdueToday));
    }
}
