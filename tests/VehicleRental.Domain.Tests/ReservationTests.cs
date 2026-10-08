using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.Domain.Tests;

public class ReservationTests
{
    private static readonly DateOnly Today = TestData.Oct1;
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static Reservation Reserve(
        DateOnly? start = null,
        DateOnly? end = null,
        Vehicle? vehicle = null,
        IVehiclePricingStrategy? strategy = null)
    {
        DateOnly from = start ?? Today.AddDays(10);

        return Reservation.Create(
            TestData.CreateCustomer(),
            vehicle ?? TestData.CreateVehicle(),
            from,
            end ?? from.AddDays(3),
            Today,
            Now,
            strategy ?? new NormalPricingStrategy());
    }

    // ----- Creation and the quote -----

    [Fact]
    public void Create_StartsActive_AndStoresTheQuote()
    {
        Vehicle vehicle = TestData.CreateVehicle(dailyRate: 100m);
        DateOnly start = Today.AddDays(10);

        Reservation reservation = Reserve(start, start.AddDays(3), vehicle);

        Assert.NotEqual(Guid.Empty, reservation.Id);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(start, reservation.StartDate);
        Assert.Equal(start.AddDays(3), reservation.EndDate);
        Assert.Equal(Now, reservation.CreatedAt);
        Assert.Equal(3, reservation.BillableDays);
        Assert.Equal(100m, reservation.DailyRateAtReservation);
        Assert.Equal(300m, reservation.TotalCost);
        Assert.Equal("Normal pricing", reservation.PricingDescription);
        Assert.Null(reservation.RentalId);
        Assert.Null(reservation.CancelledAt);
        Assert.Null(reservation.FulfilledAt);
    }

    [Fact]
    public void Create_DoesNotChangeTheVehicle()
    {
        Vehicle vehicle = TestData.CreateVehicle();

        Reserve(vehicle: vehicle);

        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(3, 300)]
    [InlineData(6, 600)]
    [InlineData(7, 560)]   // long-term pricing from 7 days: 20% off
    [InlineData(10, 800)]
    public void Create_UsesThePricingPolicy_ForTheBillableDays(int days, int expectedTotal)
    {
        DateOnly start = Today.AddDays(5);
        var strategy = PricingPolicy.SelectStrategy(days, promotionalDiscountRequested: false);

        Reservation reservation = Reserve(start, start.AddDays(days), strategy: strategy);

        Assert.Equal(days, reservation.BillableDays);
        Assert.Equal(expectedTotal, reservation.TotalCost);
    }

    [Fact]
    public void Create_WithThePromotion_StoresTheDiscountedQuoteAndItsDescription()
    {
        DateOnly start = Today.AddDays(5);
        var strategy = PricingPolicy.SelectStrategy(3, promotionalDiscountRequested: true);

        Reservation reservation = Reserve(start, start.AddDays(3), strategy: strategy);

        Assert.Equal(270m, reservation.TotalCost);
        Assert.Equal("Promotional discount (10%)", reservation.PricingDescription);
    }

    // ----- Date rules -----

    [Fact]
    public void Create_CanStartToday()
    {
        Reservation reservation = Reserve(Today, Today.AddDays(1));

        Assert.Equal(Today, reservation.StartDate);
        Assert.Equal(1, reservation.BillableDays);
    }

    [Fact]
    public void Create_RejectsAStartDateInThePast()
    {
        var ex = Assert.Throws<ArgumentException>(() => Reserve(Today.AddDays(-1), Today.AddDays(2)));

        Assert.Contains("past", ex.Message);
    }

    [Fact]
    public void Create_AllowsTheFurthestBookingDate_AndRejectsOneDayBeyondIt()
    {
        DateOnly furthest = Today.AddDays(Reservation.MaxAdvanceBookingDays);

        Assert.Equal(furthest, Reserve(furthest, furthest.AddDays(2)).StartDate);
        Assert.Throws<ArgumentException>(() => Reserve(furthest.AddDays(1), furthest.AddDays(3)));
    }

    [Fact]
    public void Create_RejectsAnEndDateThatIsNotAfterTheStart()
    {
        DateOnly start = Today.AddDays(5);

        Assert.Throws<ArgumentException>(() => Reserve(start, start));
        Assert.Throws<ArgumentException>(() => Reserve(start, start.AddDays(-1)));
    }

    [Fact]
    public void Create_AllowsTheLongestReservation_AndRejectsOneDayLonger()
    {
        DateOnly start = Today.AddDays(5);

        Assert.Equal(Reservation.MaxReservationDays,
            Reserve(start, start.AddDays(Reservation.MaxReservationDays)).BillableDays);
        Assert.Throws<ArgumentException>(() => Reserve(start, start.AddDays(Reservation.MaxReservationDays + 1)));
    }

    [Fact]
    public void Create_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Reservation.Create(
            null!, TestData.CreateVehicle(), Today, Today.AddDays(1), Today, Now, new NormalPricingStrategy()));
        Assert.Throws<ArgumentNullException>(() => Reservation.Create(
            TestData.CreateCustomer(), null!, Today, Today.AddDays(1), Today, Now, new NormalPricingStrategy()));
        Assert.Throws<ArgumentNullException>(() => Reservation.Create(
            TestData.CreateCustomer(), TestData.CreateVehicle(), Today, Today.AddDays(1), Today, Now, null!));
    }

    // ----- The half-open interval [start, end) -----

    [Theory]
    [InlineData(10, 13, 10, 13, true)]   // identical
    [InlineData(10, 13, 12, 15, true)]   // partial overlap at the end
    [InlineData(10, 13, 8, 11, true)]    // partial overlap at the start
    [InlineData(10, 20, 12, 14, true)]   // one inside the other
    [InlineData(12, 14, 10, 20, true)]
    [InlineData(10, 13, 13, 16, false)]  // second starts the day the first ends: touching, not overlapping
    [InlineData(13, 16, 10, 13, false)]  // and the other way round
    [InlineData(10, 13, 14, 16, false)]  // a gap between them
    [InlineData(10, 11, 11, 12, false)]  // adjacent one-day bookings
    [InlineData(10, 11, 10, 11, true)]
    public void Overlaps_UsesAHalfOpenInterval(int startA, int endA, int startB, int endB, bool expected)
    {
        DateOnly origin = Today;

        Assert.Equal(expected, Reservation.Overlaps(
            origin.AddDays(startA), origin.AddDays(endA), origin.AddDays(startB), origin.AddDays(endB)));
    }

    [Fact]
    public void OverlapsPeriod_ComparesWithTheReservationDates()
    {
        Reservation reservation = Reserve(Today.AddDays(10), Today.AddDays(13));

        Assert.True(reservation.OverlapsPeriod(Today.AddDays(12), Today.AddDays(14)));
        Assert.False(reservation.OverlapsPeriod(Today.AddDays(13), Today.AddDays(15)));
        Assert.False(reservation.OverlapsPeriod(Today.AddDays(8), Today.AddDays(10)));
    }

    // ----- Cancelling -----

    [Fact]
    public void Cancel_MovesAnActiveReservationToCancelled_AndRecordsWhen()
    {
        Reservation reservation = Reserve();
        DateTimeOffset later = Now.AddHours(5);

        reservation.Cancel(later);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(later, reservation.CancelledAt);
        Assert.False(reservation.CanBeCancelled);
    }

    [Fact]
    public void Cancel_TwiceIsRejected()
    {
        Reservation reservation = Reserve();
        reservation.Cancel(Now);

        var ex = Assert.Throws<InvalidOperationException>(() => reservation.Cancel(Now));

        Assert.Contains("cancelled", ex.Message);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public void Cancel_AFulfilledReservationIsRejected()
    {
        Reservation reservation = Reserve(Today, Today.AddDays(3));
        reservation.PickUp(Today, Now);

        Assert.Throws<InvalidOperationException>(() => reservation.Cancel(Now));
        Assert.Equal(ReservationStatus.Fulfilled, reservation.Status);
    }

    [Fact]
    public void CanBeCancelledByCustomer_OnlyBeforeTheStartDate()
    {
        Reservation reservation = Reserve(Today.AddDays(2), Today.AddDays(4));

        Assert.True(reservation.CanBeCancelledByCustomer(Today));
        Assert.True(reservation.CanBeCancelledByCustomer(Today.AddDays(1)));
        Assert.False(reservation.CanBeCancelledByCustomer(Today.AddDays(2)));   // the start day itself
        Assert.False(reservation.CanBeCancelledByCustomer(Today.AddDays(3)));
        Assert.True(reservation.CanBeCancelled);                                // staff still can
    }

    // ----- Pickup -----

    [Fact]
    public void PickUp_OnTheStartDate_StartsARentalThatHonoursTheQuote()
    {
        Vehicle vehicle = TestData.CreateVehicle(dailyRate: 100m);
        DateOnly start = Today.AddDays(2);
        Reservation reservation = Reserve(start, start.AddDays(4), vehicle);
        DateTimeOffset pickupTime = Now.AddDays(2);

        Rental rental = reservation.PickUp(start, pickupTime);

        Assert.Equal(ReservationStatus.Fulfilled, reservation.Status);
        Assert.Equal(pickupTime, reservation.FulfilledAt);
        Assert.Equal(rental.Id, reservation.RentalId);

        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Same(reservation.Customer, rental.Customer);
        Assert.Same(vehicle, rental.Vehicle);
        Assert.Equal(start, rental.StartDate);
        Assert.Equal(reservation.EndDate, rental.ExpectedReturnDate);
        Assert.Equal(reservation.DailyRateAtReservation, rental.DailyRateAtRental);
        Assert.Equal(reservation.BillableDays, rental.BillableDays);
        Assert.Equal(reservation.PricingDescription, rental.PricingDescription);
        Assert.Equal(reservation.TotalCost, rental.TotalCost);
        Assert.Equal(VehicleAvailabilityStatus.Rented, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void PickUp_Later_StillHonoursTheOriginalQuote()
    {
        DateOnly start = Today.AddDays(2);
        Reservation reservation = Reserve(start, start.AddDays(4));

        Rental rental = reservation.PickUp(start.AddDays(2), Now);   // two days late, still before the end date

        Assert.Equal(start.AddDays(2), rental.StartDate);
        Assert.Equal(start.AddDays(4), rental.ExpectedReturnDate);
        Assert.Equal(400m, rental.TotalCost);
        Assert.Equal(4, rental.BillableDays);
    }

    [Fact]
    public void PickUp_TheRentalCanBeCompletedAsUsual()
    {
        Vehicle vehicle = TestData.CreateVehicle();
        Reservation reservation = Reserve(Today, Today.AddDays(3), vehicle);
        Rental rental = reservation.PickUp(Today, Now);

        rental.Complete(Today.AddDays(2));

        Assert.Equal(RentalStatus.Completed, rental.Status);
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void PickUp_BeforeTheStartDate_IsRejected_AndNothingChanges()
    {
        Vehicle vehicle = TestData.CreateVehicle();
        Reservation reservation = Reserve(Today.AddDays(5), Today.AddDays(8), vehicle);

        Assert.Throws<InvalidOperationException>(() => reservation.PickUp(Today.AddDays(4), Now));

        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Null(reservation.RentalId);
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void PickUp_OnOrAfterTheEndDate_IsRejected()
    {
        Reservation reservation = Reserve(Today.AddDays(5), Today.AddDays(8));

        Assert.Throws<InvalidOperationException>(() => reservation.PickUp(Today.AddDays(8), Now));
        Assert.Throws<InvalidOperationException>(() => reservation.PickUp(Today.AddDays(9), Now));
        Assert.Equal(ReservationStatus.Active, reservation.Status);
    }

    [Fact]
    public void PickUp_OnTheLastDayBeforeTheEnd_IsAllowed()
    {
        Reservation reservation = Reserve(Today.AddDays(5), Today.AddDays(8));

        Rental rental = reservation.PickUp(Today.AddDays(7), Now);

        Assert.Equal(Today.AddDays(7), rental.StartDate);
        Assert.Equal(Today.AddDays(8), rental.ExpectedReturnDate);
    }

    [Fact]
    public void PickUp_WhenTheVehicleIsStillRentedOut_IsRejected_AndTheReservationStaysActive()
    {
        Vehicle vehicle = TestData.CreateVehicle();
        Reservation reservation = Reserve(Today, Today.AddDays(3), vehicle);
        vehicle.MarkAsRented();

        Assert.Throws<InvalidOperationException>(() => reservation.PickUp(Today, Now));

        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Null(reservation.RentalId);
        Assert.Null(reservation.FulfilledAt);
    }

    [Fact]
    public void PickUp_Twice_IsRejected()
    {
        Vehicle vehicle = TestData.CreateVehicle();
        Reservation reservation = Reserve(Today, Today.AddDays(3), vehicle);
        Rental first = reservation.PickUp(Today, Now);

        var ex = Assert.Throws<InvalidOperationException>(() => reservation.PickUp(Today, Now));

        Assert.Contains("fulfilled", ex.Message);
        Assert.Equal(first.Id, reservation.RentalId);
    }

    [Fact]
    public void PickUp_ACancelledReservation_IsRejected()
    {
        Reservation reservation = Reserve(Today, Today.AddDays(3));
        reservation.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => reservation.PickUp(Today, Now));
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    // ----- Expiry (a no-show: still active after its period is over) -----

    [Fact]
    public void IsExpired_BecomesTrueOnTheEndDate_NotTheDayBefore()
    {
        Reservation reservation = Reserve(Today.AddDays(2), Today.AddDays(5));

        Assert.False(reservation.IsExpired(Today));
        Assert.False(reservation.IsExpired(Today.AddDays(2)));   // the start day
        Assert.False(reservation.IsExpired(Today.AddDays(4)));   // the last day it holds the vehicle
        Assert.True(reservation.IsExpired(Today.AddDays(5)));    // the end date: the vehicle is free again
        Assert.True(reservation.IsExpired(Today.AddDays(30)));
    }

    [Fact]
    public void IsExpired_IsFalseOnceCancelledOrFulfilled()
    {
        Reservation cancelled = Reserve(Today, Today.AddDays(2));
        cancelled.Cancel(Now);
        Reservation fulfilled = Reserve(Today, Today.AddDays(2));
        fulfilled.PickUp(Today, Now);

        Assert.False(cancelled.IsExpired(Today.AddDays(10)));
        Assert.False(fulfilled.IsExpired(Today.AddDays(10)));
    }

    [Fact]
    public void AnExpiredReservation_CannotBePickedUp_ButCanStillBeCancelledByTheBusiness()
    {
        Reservation reservation = Reserve(Today, Today.AddDays(2));
        DateOnly later = Today.AddDays(2);

        Assert.True(reservation.IsExpired(later));
        Assert.Throws<InvalidOperationException>(() => reservation.PickUp(later, Now));
        Assert.True(reservation.CanBeCancelled);
        Assert.False(reservation.CanBeCancelledByCustomer(later));
    }
}

