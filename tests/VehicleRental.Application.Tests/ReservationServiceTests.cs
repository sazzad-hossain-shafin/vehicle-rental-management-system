using VehicleRental.Application.Customers;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests;

/// <summary>
/// The reservation use cases against in-memory repositories and a clock fixed at 1 October 2026, 12:00 UTC.
/// The database-level guarantees (the exclusion constraint and concurrency) are tested against PostgreSQL elsewhere.
/// </summary>
public class ReservationServiceTests
{
    private static readonly DateOnly Today = TestApp.Today;

    private readonly TestApp _app = new();

    private async Task<(VehicleDto Vehicle, CustomerDto Alice, CustomerDto Bob)> SetUpAsync()
    {
        var vehicle = await _app.AddVehicleAsync("V1");
        var alice = await _app.Customers.CreateAsync("C1", "Alice");
        var bob = await _app.Customers.CreateAsync("C2", "Bob");

        return (vehicle, alice, bob);
    }

    private Task<ReservationDto> ReserveAsync(
        Guid customerId,
        Guid vehicleId,
        int startOffset,
        int endOffset,
        bool promotion = false) =>
        _app.Reservations.CreateAsync(new CreateReservationRequest(
            customerId, vehicleId, Today.AddDays(startOffset), Today.AddDays(endOffset), promotion));

    // ----- Creating -----

    [Fact]
    public async Task Create_StoresAnActiveReservation_WithTheQuote_AndDoesNotRentTheVehicle()
    {
        var (vehicle, alice, _) = await SetUpAsync();

        ReservationDto reservation = await ReserveAsync(alice.Id, vehicle.Id, 5, 8);

        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(alice.Id, reservation.CustomerId);
        Assert.Equal("C1", reservation.CustomerNumber);
        Assert.Equal(vehicle.Id, reservation.VehicleId);
        Assert.Equal(Today.AddDays(5), reservation.StartDate);
        Assert.Equal(Today.AddDays(8), reservation.EndDate);
        Assert.Equal(3, reservation.BillableDays);
        Assert.Equal(100m, reservation.DailyRateAtReservation);
        Assert.Equal(300m, reservation.TotalCost);
        Assert.Equal(_app.Clock.GetUtcNow(), reservation.CreatedAt);

        // A reservation is not a checked-out vehicle.
        Assert.Equal(VehicleAvailabilityStatus.Available, (await _app.Vehicles.GetByIdAsync(vehicle.Id)).AvailabilityStatus);
        Assert.Equal(0, _app.RentalRepository.Count);
        Assert.Single(_app.ReservationRepository.All);
    }

    [Fact]
    public async Task Create_ThePromotionApplies_ToShortBookings_ButNotOverTheLongTermDiscount()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        var second = await _app.AddVehicleAsync("V2");

        ReservationDto promo = await ReserveAsync(alice.Id, vehicle.Id, 5, 8, promotion: true);
        ReservationDto longTerm = await ReserveAsync(alice.Id, second.Id, 5, 12, promotion: true);

        Assert.Equal(270m, promo.TotalCost);
        Assert.Contains("Promotional", promo.PricingDescription);
        Assert.Equal(560m, longTerm.TotalCost);   // 7 days: long-term wins, discounts never stack
        Assert.Contains("Long-term", longTerm.PricingDescription);
    }

    [Fact]
    public async Task Create_UnknownVehicleOrCustomer_IsNotFound()
    {
        var (vehicle, alice, _) = await SetUpAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => ReserveAsync(alice.Id, Guid.NewGuid(), 5, 8));
        await Assert.ThrowsAsync<NotFoundException>(() => ReserveAsync(Guid.NewGuid(), vehicle.Id, 5, 8));
        Assert.Empty(_app.ReservationRepository.All);
    }

    [Fact]
    public async Task Create_RejectsBadDates()
    {
        var (vehicle, alice, _) = await SetUpAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(alice.Id, vehicle.Id, -1, 2));   // past
        await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(alice.Id, vehicle.Id, 5, 5));    // empty
        await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(alice.Id, vehicle.Id, 5, 3));    // backwards
        await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(alice.Id, vehicle.Id, 1000, 1002));
        Assert.Empty(_app.ReservationRepository.All);
    }

    // ----- Availability and overlap -----

    [Fact]
    public async Task Create_OverlappingActiveReservation_IsAConflict()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        await ReserveAsync(alice.Id, vehicle.Id, 5, 8);

        foreach ((int start, int end) in new[] { (5, 8), (6, 7), (4, 6), (7, 10), (3, 12) })
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => ReserveAsync(bob.Id, vehicle.Id, start, end));
            Assert.Contains("not available", ex.Message);
        }

        Assert.Single(_app.ReservationRepository.All);
    }

    [Fact]
    public async Task Create_AdjacentReservations_AreNotAConflict_InEitherOrder()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        await ReserveAsync(alice.Id, vehicle.Id, 5, 8);

        await ReserveAsync(bob.Id, vehicle.Id, 8, 10);   // starts the day the first ends
        await ReserveAsync(bob.Id, vehicle.Id, 3, 5);    // ends the day the first starts

        Assert.Equal(3, _app.ReservationRepository.All.Count);
    }

    [Fact]
    public async Task Create_AnotherVehicle_OnTheSameDates_IsFine()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        var other = await _app.AddVehicleAsync("V2");
        await ReserveAsync(alice.Id, vehicle.Id, 5, 8);

        await ReserveAsync(bob.Id, other.Id, 5, 8);

        Assert.Equal(2, _app.ReservationRepository.All.Count);
    }

    [Fact]
    public async Task ACancelledReservation_NoLongerHoldsTheVehicle()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        ReservationDto first = await ReserveAsync(alice.Id, vehicle.Id, 5, 8);
        await _app.Reservations.CancelAsync(first.Id);

        await ReserveAsync(bob.Id, vehicle.Id, 5, 8);

        Assert.Equal(2, _app.ReservationRepository.All.Count);
    }

    [Fact]
    public async Task AnActiveRental_BlocksReservationsOverlappingItsDays_ButNotOnesThatStartWhenItEnds()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, alice.Id, 5));   // Oct 1 to Oct 6

        await Assert.ThrowsAsync<ConflictException>(() => ReserveAsync(bob.Id, vehicle.Id, 2, 4));
        await Assert.ThrowsAsync<ConflictException>(() => ReserveAsync(bob.Id, vehicle.Id, 4, 8));
        await Assert.ThrowsAsync<ConflictException>(() => ReserveAsync(bob.Id, vehicle.Id, 0, 2));

        await ReserveAsync(bob.Id, vehicle.Id, 5, 8);   // Oct 6 to Oct 9: starts the day the rental is due back
    }

    [Fact]
    public async Task AnOverdueRental_StillBlocksToday_ButNotLaterDays()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, alice.Id, 2));   // due back Oct 3
        _app.Clock.AdvanceDays(10);                                                                 // now Oct 11, still out

        DateOnly today = Today.AddDays(10);

        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.CreateAsync(
            new CreateReservationRequest(bob.Id, vehicle.Id, today, today.AddDays(2))));

        // From tomorrow it is assumed to be back.
        await _app.Reservations.CreateAsync(
            new CreateReservationRequest(bob.Id, vehicle.Id, today.AddDays(1), today.AddDays(3)));
    }

    [Fact]
    public async Task AWalkInRental_CannotTakeAVehicleReservedForItsDays()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        await ReserveAsync(bob.Id, vehicle.Id, 2, 5);   // Oct 3 to Oct 6

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, alice.Id, 4)));   // Oct 1 to Oct 5

        Assert.Contains("reserved", ex.Message);
        Assert.Equal(0, _app.RentalRepository.Count);
        Assert.Equal(VehicleAvailabilityStatus.Available, (await _app.Vehicles.GetByIdAsync(vehicle.Id)).AvailabilityStatus);
    }

    [Fact]
    public async Task AWalkInRental_ThatEndsWhenTheReservationStarts_IsFine()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        await ReserveAsync(bob.Id, vehicle.Id, 2, 5);   // from Oct 3

        await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, alice.Id, 2));   // Oct 1 to Oct 3

        Assert.Equal(1, _app.RentalRepository.Count);
    }

    [Fact]
    public async Task ACancelledReservation_NoLongerBlocksAWalkInRental()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        ReservationDto reservation = await ReserveAsync(bob.Id, vehicle.Id, 0, 3);
        await _app.Reservations.CancelAsync(reservation.Id);

        await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, alice.Id, 2));

        Assert.Equal(1, _app.RentalRepository.Count);
    }

    [Fact]
    public async Task Availability_ListsOnlyVehiclesFreeForTheWholePeriod_OrderedByRegistration()
    {
        var (v1, alice, _) = await SetUpAsync();
        var v2 = await _app.AddVehicleAsync("V2");
        var v3 = await _app.AddVehicleAsync("V3", type: VehicleType.Van);
        var v4 = await _app.AddVehicleAsync("V4", dailyRate: 40m);
        await ReserveAsync(alice.Id, v2.Id, 6, 8);

        var all = await _app.Reservations.GetAvailableVehiclesPageAsync(Today.AddDays(5), Today.AddDays(7));
        var vans = await _app.Reservations.GetAvailableVehiclesPageAsync(
            Today.AddDays(5), Today.AddDays(7), VehicleType.Van);
        var cheap = await _app.Reservations.GetAvailableVehiclesPageAsync(
            Today.AddDays(5), Today.AddDays(7), maximumDailyRate: 50m);
        var after = await _app.Reservations.GetAvailableVehiclesPageAsync(Today.AddDays(8), Today.AddDays(9));

        Assert.Equal(new[] { v1.Id, v3.Id, v4.Id }, all.Items.Select(v => v.Id));
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(new[] { v3.Id }, vans.Items.Select(v => v.Id));
        Assert.Equal(new[] { v4.Id }, cheap.Items.Select(v => v.Id));
        Assert.Equal(4, after.TotalCount);   // the reservation ends on Oct 9, so the period starting Oct 9 is free
    }

    [Fact]
    public async Task Availability_Pages_AndRejectsBadInput()
    {
        await SetUpAsync();
        await _app.AddVehicleAsync("V2");
        await _app.AddVehicleAsync("V3");

        var page2 = await _app.Reservations.GetAvailableVehiclesPageAsync(
            Today.AddDays(5), Today.AddDays(7), page: 2, pageSize: 2);

        Assert.Single(page2.Items);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page2.TotalPages);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _app.Reservations.GetAvailableVehiclesPageAsync(Today.AddDays(5), Today.AddDays(5)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _app.Reservations.GetAvailableVehiclesPageAsync(Today.AddDays(-2), Today.AddDays(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _app.Reservations.GetAvailableVehiclesPageAsync(Today.AddDays(5), Today.AddDays(7), pageSize: 101));
    }

    // ----- Time boundaries -----

    [Fact]
    public async Task Create_CanStartToday_UntilTheDayChanges()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        var other = await _app.AddVehicleAsync("V2");

        _app.Clock.Advance(TimeSpan.FromHours(11) + TimeSpan.FromMinutes(59));   // 23:59 on Oct 1
        await ReserveAsync(alice.Id, vehicle.Id, 0, 1);

        _app.Clock.Advance(TimeSpan.FromMinutes(1));                             // 00:00 on Oct 2
        await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(bob.Id, other.Id, 0, 1));
        await ReserveAsync(bob.Id, other.Id, 1, 2);
    }

    // ----- Reading and ownership -----

    [Fact]
    public async Task Get_AndList_ReturnReservations_EarliestStartFirst()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        var other = await _app.AddVehicleAsync("V2");
        ReservationDto later = await ReserveAsync(alice.Id, vehicle.Id, 10, 12);
        ReservationDto sooner = await ReserveAsync(bob.Id, other.Id, 3, 5);

        var page = await _app.Reservations.GetPageAsync();
        var one = await _app.Reservations.GetAsync(later.Id);

        Assert.Equal(new[] { sooner.Id, later.Id }, page.Items.Select(r => r.Id));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(later.Id, one.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Reservations.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task List_CanBeFilteredByStatus()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        ReservationDto a = await ReserveAsync(alice.Id, vehicle.Id, 3, 5);
        await ReserveAsync(alice.Id, vehicle.Id, 8, 10);
        await _app.Reservations.CancelAsync(a.Id);

        var active = await _app.Reservations.GetPageAsync(ReservationStatus.Active);
        var cancelled = await _app.Reservations.GetPageAsync(ReservationStatus.Cancelled);
        var fulfilled = await _app.Reservations.GetPageAsync(ReservationStatus.Fulfilled);

        Assert.Equal(1, active.TotalCount);
        Assert.Equal(a.Id, Assert.Single(cancelled.Items).Id);
        Assert.Empty(fulfilled.Items);
    }

    [Fact]
    public async Task ACustomer_SeesOnlyTheirOwnReservations()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        var other = await _app.AddVehicleAsync("V2");
        ReservationDto mine = await ReserveAsync(alice.Id, vehicle.Id, 3, 5);
        ReservationDto theirs = await ReserveAsync(bob.Id, other.Id, 3, 5);

        var alicesPage = await _app.Reservations.GetPageForCustomerAsync(alice.Id);
        var found = await _app.Reservations.GetForCustomerAsync(mine.Id, alice.Id);

        Assert.Equal(mine.Id, Assert.Single(alicesPage.Items).Id);
        Assert.Equal(1, alicesPage.TotalCount);
        Assert.Equal(mine.Id, found.Id);

        // Someone else's reservation looks exactly like one that does not exist.
        Guid missingId = Guid.NewGuid();
        var notOwned = await Assert.ThrowsAsync<NotFoundException>(() => _app.Reservations.GetForCustomerAsync(theirs.Id, alice.Id));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => _app.Reservations.GetForCustomerAsync(missingId, alice.Id));
        Assert.Equal($"Reservation '{theirs.Id}' was not found.", notOwned.Message);
        Assert.Equal($"Reservation '{missingId}' was not found.", missing.Message);
    }

    // ----- Cancelling -----

    [Fact]
    public async Task ACustomer_CanCancelTheirOwnFutureReservation()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        ReservationDto reservation = await ReserveAsync(alice.Id, vehicle.Id, 3, 5);

        ReservationDto cancelled = await _app.Reservations.CancelForCustomerAsync(reservation.Id, alice.Id);

        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);
    }

    [Fact]
    public async Task ACustomer_CannotCancelSomeoneElsesReservation_AndItStaysActive()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        ReservationDto reservation = await ReserveAsync(alice.Id, vehicle.Id, 3, 5);

        await Assert.ThrowsAsync<NotFoundException>(() => _app.Reservations.CancelForCustomerAsync(reservation.Id, bob.Id));

        Assert.Equal(ReservationStatus.Active, (await _app.Reservations.GetAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task ACustomer_CannotCancelOnOrAfterTheStartDate_ButStaffCan()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        ReservationDto reservation = await ReserveAsync(alice.Id, vehicle.Id, 2, 5);
        _app.Clock.AdvanceDays(2);   // the start day

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _app.Reservations.CancelForCustomerAsync(reservation.Id, alice.Id));
        Assert.Contains("rental desk", ex.Message);

        ReservationDto cancelled = await _app.Reservations.CancelAsync(reservation.Id);
        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Cancelling_TwiceOrAFulfilledReservation_IsAConflict()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        var other = await _app.AddVehicleAsync("V2");
        ReservationDto a = await ReserveAsync(alice.Id, vehicle.Id, 3, 5);
        ReservationDto b = await ReserveAsync(alice.Id, other.Id, 0, 3);
        await _app.Reservations.CancelAsync(a.Id);
        await _app.Reservations.PickUpAsync(b.Id);

        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.CancelAsync(a.Id));
        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.CancelForCustomerAsync(a.Id, alice.Id));
        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.CancelAsync(b.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Reservations.CancelAsync(Guid.NewGuid()));
    }

    // ----- Pickup -----

    [Fact]
    public async Task PickUp_OnTheStartDate_StartsTheRental_AndFulfilsTheReservation()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        ReservationDto reservation = await ReserveAsync(alice.Id, vehicle.Id, 0, 3);

        PickupResultDto result = await _app.Reservations.PickUpAsync(reservation.Id);

        Assert.Equal(ReservationStatus.Fulfilled, result.Reservation.Status);
        Assert.Equal(result.Rental.Id, result.Reservation.RentalId);
        Assert.NotNull(result.Reservation.FulfilledAt);

        Assert.Equal(RentalStatus.Active, result.Rental.Status);
        Assert.Equal(alice.Id, result.Rental.CustomerId);
        Assert.Equal(vehicle.Id, result.Rental.VehicleId);
        Assert.Equal(Today, result.Rental.StartDate);
        Assert.Equal(Today.AddDays(3), result.Rental.ExpectedReturnDate);
        Assert.Equal(reservation.TotalCost, result.Rental.TotalCost);
        Assert.Equal(reservation.DailyRateAtReservation, result.Rental.DailyRateAtRental);
        Assert.Equal(reservation.PricingDescription, result.Rental.PricingDescription);

        Assert.Equal(VehicleAvailabilityStatus.Rented, (await _app.Vehicles.GetByIdAsync(vehicle.Id)).AvailabilityStatus);
        Assert.Equal(1, _app.RentalRepository.Count);
    }

    [Fact]
    public async Task PickUp_PreservesTheHistoricalQuote()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        ReservationDto reservation = await ReserveAsync(alice.Id, vehicle.Id, 1, 8);   // 7 days: long-term, 560
        _app.Clock.AdvanceDays(3);                                                      // picked up three days late

        PickupResultDto result = await _app.Reservations.PickUpAsync(reservation.Id);

        Assert.Equal(560m, result.Rental.TotalCost);
        Assert.Equal(7, result.Rental.BillableDays);
        Assert.Contains("Long-term", result.Rental.PricingDescription);
        Assert.Equal(560m, (await _app.Reservations.GetAsync(reservation.Id)).TotalCost);
    }

    [Fact]
    public async Task PickUp_TwiceIsAConflict_AndNeverCreatesASecondRental()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        ReservationDto reservation = await ReserveAsync(alice.Id, vehicle.Id, 0, 3);
        await _app.Reservations.PickUpAsync(reservation.Id);

        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.PickUpAsync(reservation.Id));

        Assert.Equal(1, _app.RentalRepository.Count);
    }

    [Fact]
    public async Task PickUp_BeforeTheStartDate_OrAfterTheEnd_OrAfterCancelling_IsAConflict()
    {
        var (vehicle, alice, _) = await SetUpAsync();
        var other = await _app.AddVehicleAsync("V2");
        ReservationDto early = await ReserveAsync(alice.Id, vehicle.Id, 3, 5);
        ReservationDto cancelled = await ReserveAsync(alice.Id, other.Id, 0, 2);
        await _app.Reservations.CancelAsync(cancelled.Id);

        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.PickUpAsync(early.Id));
        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.PickUpAsync(cancelled.Id));

        _app.Clock.AdvanceDays(5);   // Oct 6: the reservation ended on Oct 6 and cannot be picked up any more
        await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.PickUpAsync(early.Id));

        Assert.Equal(0, _app.RentalRepository.Count);
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Reservations.PickUpAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task PickUp_WhileTheVehicleIsStillOut_IsAConflict_AndTheReservationStaysActive()
    {
        var (vehicle, alice, bob) = await SetUpAsync();
        await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, alice.Id, 2));   // out until Oct 3
        ReservationDto reservation = await ReserveAsync(bob.Id, vehicle.Id, 2, 5);                  // from Oct 3
        _app.Clock.AdvanceDays(2);                                                                  // Oct 3: due, but not returned

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _app.Reservations.PickUpAsync(reservation.Id));

        Assert.Contains("not available", ex.Message);
        Assert.Equal(ReservationStatus.Active, (await _app.Reservations.GetAsync(reservation.Id)).Status);
        Assert.Equal(1, _app.RentalRepository.Count);

        // Once it is returned the pickup works.
        await _app.Rentals.ReturnVehicleAsync("V1");
        PickupResultDto result = await _app.Reservations.PickUpAsync(reservation.Id);
        Assert.Equal(ReservationStatus.Fulfilled, result.Reservation.Status);
    }
}
