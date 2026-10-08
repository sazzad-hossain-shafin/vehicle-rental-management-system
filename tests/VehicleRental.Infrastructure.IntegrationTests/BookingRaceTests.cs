using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// The cross-table race: a walk-in rental and a reservation for the same vehicle and overlapping days, submitted at
/// the same moment. They live in different tables, so no single constraint can see both; the guarantee comes from
/// every booking operation first taking a row lock on the vehicle inside its transaction, which makes the operations
/// on one vehicle run one after another and lets each one see the other's committed result.
/// </summary>
public class BookingRaceTests : DatabaseTestBase
{
    // The services use the machine's local date, so these tests do too.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public BookingRaceTests(PostgresFixture database) : base(database)
    {
    }

    private async Task<(Guid VehicleId, Guid AliceId, Guid BobId)> SeedAsync(string registration = "ABC-123")
    {
        var vehicle = TestEntities.NewVehicle(registration);
        var alice = TestEntities.NewCustomer("C1", "Alice");
        var bob = TestEntities.NewCustomer("C2", "Bob");

        await using var session = Database.CreateSession();
        await session.Vehicles.AddAsync(vehicle);
        await session.Customers.AddAsync(alice);
        await session.Customers.AddAsync(bob);
        await session.UnitOfWork.SaveChangesAsync();

        return (vehicle.Id, alice.Id, bob.Id);
    }

    private (ReservationService Reservations, RentalService Rentals, IAsyncDisposable Session) NewServices()
    {
        var session = Database.CreateSession();

        return (
            new ReservationService(
                session.Vehicles, session.Customers, session.Reservations,
                session.Rentals, session.Availability, session.UnitOfWork),
            new RentalService(
                session.Vehicles, session.Customers, session.Rentals, session.Reservations, session.UnitOfWork),
            session);
    }

    private static async Task<Exception?> RunAsync(Func<Task> action)
    {
        try
        {
            await action();

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [DatabaseFact]
    public async Task AWalkInRentalAndAReservation_RacingForTheSameDays_ExactlyOneSucceeds()
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Database.ResetAsync();
            var (vehicleId, aliceId, bobId) = await SeedAsync();
            using var barrier = new Barrier(2);

            // The walk-in rental covers today to day 3; the reservation covers day 1 to day 4: they overlap.
            Task<Exception?> walkIn = Task.Run(async () =>
            {
                var (_, rentals, session) = NewServices();
                await using (session)
                {
                    barrier.SignalAndWait();

                    return await RunAsync(() => rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, aliceId, 3)));
                }
            });

            Task<Exception?> reservation = Task.Run(async () =>
            {
                var (reservations, _, session) = NewServices();
                await using (session)
                {
                    barrier.SignalAndWait();

                    return await RunAsync(() => reservations.CreateAsync(
                        new CreateReservationRequest(bobId, vehicleId, Today.AddDays(1), Today.AddDays(4))));
                }
            });

            Exception?[] results = await Task.WhenAll(walkIn, reservation);

            await using var context = Database.CreateContext();
            int rentals = await context.Rentals.CountAsync(r => r.Status == RentalStatus.Active);
            int reservations = await context.Reservations.CountAsync(r => r.Status == ReservationStatus.Active);

            Assert.True(
                rentals + reservations == 1,
                $"Attempt {attempt}: expected exactly one claim on the vehicle, found {rentals} rental(s) and {reservations} reservation(s).");
            Assert.Equal(1, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.IsType<ConflictException>(r));
        }
    }

    [DatabaseFact]
    public async Task ManyMixedRequestsForOverlappingDays_ExactlyOneClaimSucceeds()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Database.ResetAsync();
            var (vehicleId, aliceId, bobId) = await SeedAsync();
            using var barrier = new Barrier(6);

            Task<Exception?> Walk(Guid customerId, int days) => Task.Run(async () =>
            {
                var (_, rentals, session) = NewServices();
                await using (session)
                {
                    barrier.SignalAndWait();

                    return await RunAsync(() => rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, customerId, days)));
                }
            });

            Task<Exception?> Reserve(Guid customerId, int start, int end) => Task.Run(async () =>
            {
                var (reservations, _, session) = NewServices();
                await using (session)
                {
                    barrier.SignalAndWait();

                    return await RunAsync(() => reservations.CreateAsync(
                        new CreateReservationRequest(customerId, vehicleId, Today.AddDays(start), Today.AddDays(end))));
                }
            });

            // Every pair overlaps on day 2.
            var tasks = new[]
            {
                Walk(aliceId, 3), Walk(bobId, 4), Reserve(aliceId, 1, 4), Reserve(bobId, 2, 5), Reserve(aliceId, 0, 3), Reserve(bobId, 2, 3)
            };
            Exception?[] results = await Task.WhenAll(tasks);

            await using var context = Database.CreateContext();
            int claims = await context.Rentals.CountAsync(r => r.Status == RentalStatus.Active)
                         + await context.Reservations.CountAsync(r => r.Status == ReservationStatus.Active);

            Assert.True(claims == 1, $"Attempt {attempt}: expected exactly one claim, found {claims}.");
            Assert.Equal(1, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.IsType<ConflictException>(r));
        }
    }

    [DatabaseFact]
    public async Task NonOverlappingRentalAndReservation_BothSucceed_WhateverTheOrder()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            await Database.ResetAsync();
            var (vehicleId, aliceId, bobId) = await SeedAsync();
            using var barrier = new Barrier(2);

            // The rental covers today to day 2; the reservation covers day 2 to day 5: they only touch.
            Task<Exception?> walkIn = Task.Run(async () =>
            {
                var (_, rentals, session) = NewServices();
                await using (session)
                {
                    barrier.SignalAndWait();

                    return await RunAsync(() => rentals.StartRentalAsync(new StartRentalByIdRequest(vehicleId, aliceId, 2)));
                }
            });

            Task<Exception?> reservation = Task.Run(async () =>
            {
                var (reservations, _, session) = NewServices();
                await using (session)
                {
                    barrier.SignalAndWait();

                    return await RunAsync(() => reservations.CreateAsync(
                        new CreateReservationRequest(bobId, vehicleId, Today.AddDays(2), Today.AddDays(5))));
                }
            });

            Exception?[] results = await Task.WhenAll(walkIn, reservation);

            Assert.All(results, r => Assert.Null(r));
        }
    }

    [DatabaseFact]
    public async Task TheVehicleLock_IsHeldUntilTheHolderSavesOrIsDiscarded_ThenReleased()
    {
        var (vehicleId, _, bobId) = await SeedAsync();

        // A request takes the lock and is then abandoned without saving (as after a crash or a validation failure).
        var holder = Database.CreateSession();
        await holder.UnitOfWork.LockVehicleAsync(vehicleId);

        // Another request for the same vehicle cannot proceed, and after the lock wait runs out it is told to retry.
        var (reservations, _, waiting) = NewServices();
        await using (waiting)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var ex = await Assert.ThrowsAsync<ConflictException>(() => reservations.CreateAsync(
                new CreateReservationRequest(bobId, vehicleId, Today.AddDays(1), Today.AddDays(3))));
            watch.Stop();

            Assert.Contains("try again", ex.Message);
            Assert.True(watch.Elapsed > TimeSpan.FromSeconds(3), $"It should have waited for the lock, but took {watch.Elapsed}.");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"It should have given up after the lock timeout, but took {watch.Elapsed}.");
        }

        // Disposing the abandoned request rolls its transaction back and releases the lock.
        await holder.DisposeAsync();

        var (retry, _, retrySession) = NewServices();
        await using (retrySession)
        {
            var reservation = await retry.CreateAsync(
                new CreateReservationRequest(bobId, vehicleId, Today.AddDays(1), Today.AddDays(3)));

            Assert.Equal(ReservationStatus.Active, reservation.Status);
        }
    }

    [DatabaseFact]
    public async Task AFailedRequest_ReleasesTheLock_SoTheNextOneIsNotBlocked()
    {
        var (vehicleId, aliceId, bobId) = await SeedAsync();

        var (reservations, _, session) = NewServices();
        await using (session)
        {
            await reservations.CreateAsync(new CreateReservationRequest(aliceId, vehicleId, Today.AddDays(1), Today.AddDays(4)));
        }

        // This one takes the lock, finds the vehicle taken and fails with a conflict.
        var (second, _, failedSession) = NewServices();
        await using (failedSession)
        {
            await Assert.ThrowsAsync<ConflictException>(() => second.CreateAsync(
                new CreateReservationRequest(bobId, vehicleId, Today.AddDays(2), Today.AddDays(3))));
        }

        // The failure left no lock behind: an adjacent booking goes straight through.
        var (third, _, thirdSession) = NewServices();
        await using (thirdSession)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await third.CreateAsync(new CreateReservationRequest(bobId, vehicleId, Today.AddDays(4), Today.AddDays(6)));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"The booking was blocked for {watch.Elapsed}.");
        }
    }

    [DatabaseFact]
    public async Task ALockOnOneVehicle_DoesNotBlockAnotherVehicle()
    {
        var (vehicleId, _, bobId) = await SeedAsync("AAA-111");
        Guid otherId;
        await using (var session = Database.CreateSession())
        {
            var other = TestEntities.NewVehicle("BBB-222");
            await session.Vehicles.AddAsync(other);
            await session.UnitOfWork.SaveChangesAsync();
            otherId = other.Id;
        }

        await using var holder = Database.CreateSession();
        await holder.UnitOfWork.LockVehicleAsync(vehicleId);

        var (reservations, _, session2) = NewServices();
        await using (session2)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await reservations.CreateAsync(new CreateReservationRequest(bobId, otherId, Today.AddDays(1), Today.AddDays(3)));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Another vehicle was blocked for {watch.Elapsed}.");
        }
    }
}
