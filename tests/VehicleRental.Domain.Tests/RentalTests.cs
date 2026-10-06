using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.Domain.Tests;

public class RentalTests
{
    private static readonly DateOnly Start = TestData.Oct1;

    private static Rental StartRental(
        Vehicle? vehicle = null,
        int days = 3,
        IVehiclePricingStrategy? strategy = null) =>
        Rental.Start(
            TestData.CreateCustomer(),
            vehicle ?? TestData.CreateVehicle(),
            Start,
            Start.AddDays(days),
            strategy ?? new NormalPricingStrategy());

    // ----- Creation -----

    [Fact]
    public void Start_WithValidInput_CreatesActiveRentalAndRentsTheVehicle()
    {
        var vehicle = TestData.CreateVehicle();

        var rental = StartRental(vehicle);

        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Null(rental.ActualReturnDate);
        Assert.Equal(Start, rental.StartDate);
        Assert.Equal(Start.AddDays(3), rental.ExpectedReturnDate);
        Assert.Equal(VehicleAvailabilityStatus.Rented, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void Start_StoresPriceSnapshot()
    {
        var vehicle = TestData.CreateVehicle(dailyRate: 100m);

        var rental = StartRental(vehicle, days: 5, strategy: new DiscountPricingStrategy());

        Assert.Equal(100m, rental.DailyRateAtRental);
        Assert.Equal(5, rental.BillableDays);
        Assert.Equal(450m, rental.TotalCost);
        Assert.Equal(new DiscountPricingStrategy().Name, rental.PricingDescription);
    }

    [Fact]
    public void Start_WhenVehicleAlreadyRented_ThrowsInvalidOperationException()
    {
        var vehicle = TestData.CreateVehicle();
        StartRental(vehicle);

        Assert.Throws<InvalidOperationException>(() => StartRental(vehicle));
    }

    [Fact]
    public void Start_WithNullArguments_ThrowsArgumentNullException()
    {
        var customer = TestData.CreateCustomer();
        var vehicle = TestData.CreateVehicle();
        var strategy = new NormalPricingStrategy();

        Assert.Throws<ArgumentNullException>(
            () => Rental.Start(null!, vehicle, Start, Start.AddDays(1), strategy));
        Assert.Throws<ArgumentNullException>(
            () => Rental.Start(customer, null!, Start, Start.AddDays(1), strategy));
        Assert.Throws<ArgumentNullException>(
            () => Rental.Start(customer, vehicle, Start, Start.AddDays(1), null!));
    }

    // ----- Date rules -----

    [Fact]
    public void CalculateBillableDays_FirstToFourthOfMonth_IsThreeDays()
    {
        var days = Rental.CalculateBillableDays(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4));

        Assert.Equal(3, days);
    }

    [Fact]
    public void CalculateBillableDays_ReturnOnNextDay_IsOneDay()
    {
        Assert.Equal(1, Rental.CalculateBillableDays(Start, Start.AddDays(1)));
    }

    [Fact]
    public void CalculateBillableDays_AcrossMonthBoundary_CountsCalendarDays()
    {
        var days = Rental.CalculateBillableDays(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 2));

        Assert.Equal(3, days);
    }

    [Fact]
    public void Start_WithReturnDateEqualToStartDate_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => Rental.Start(
                TestData.CreateCustomer(),
                TestData.CreateVehicle(),
                Start,
                Start,
                new NormalPricingStrategy()));
    }

    [Fact]
    public void Start_WithReturnDateBeforeStartDate_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => Rental.Start(
                TestData.CreateCustomer(),
                TestData.CreateVehicle(),
                Start,
                Start.AddDays(-2),
                new NormalPricingStrategy()));
    }

    [Fact]
    public void Start_WithInvalidDates_LeavesVehicleAvailable()
    {
        var vehicle = TestData.CreateVehicle();

        Assert.Throws<ArgumentException>(
            () => Rental.Start(
                TestData.CreateCustomer(), vehicle, Start, Start, new NormalPricingStrategy()));

        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    // ----- Price stability (regression for the original history defect) -----

    [Fact]
    public void HistoricalTotal_DoesNotChange_WhenVehicleIsReusedWithDifferentPricing()
    {
        var vehicle = TestData.CreateVehicle(dailyRate: 100m);
        var firstRental = StartRental(vehicle, days: 5, strategy: new NormalPricingStrategy());
        Assert.Equal(500m, firstRental.TotalCost);
        firstRental.Complete(Start.AddDays(5));

        // The same vehicle is rented again under a cheaper strategy.
        var secondRental = StartRental(vehicle, days: 10, strategy: new LongTermPricingStrategy());

        Assert.Equal(500m, firstRental.TotalCost);
        Assert.Equal(100m, firstRental.DailyRateAtRental);
        Assert.Equal(new NormalPricingStrategy().Name, firstRental.PricingDescription);
        Assert.Equal(800m, secondRental.TotalCost);
    }

    [Fact]
    public void TotalCost_DoesNotChange_WhenRentalIsCompleted()
    {
        var rental = StartRental(days: 5);
        var agreed = rental.TotalCost;

        rental.Complete(Start.AddDays(9));

        Assert.Equal(agreed, rental.TotalCost);
    }

    // ----- Completion -----

    [Fact]
    public void Complete_RecordsReturnDateAndMarksRentalCompleted()
    {
        var rental = StartRental();
        var returnDate = Start.AddDays(2);

        rental.Complete(returnDate);

        Assert.Equal(RentalStatus.Completed, rental.Status);
        Assert.Equal(returnDate, rental.ActualReturnDate);
    }

    [Fact]
    public void Complete_MakesTheVehicleAvailableAgain()
    {
        var vehicle = TestData.CreateVehicle();
        var rental = StartRental(vehicle);

        rental.Complete(Start.AddDays(3));

        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_ThrowsInvalidOperationException()
    {
        var rental = StartRental();
        rental.Complete(Start.AddDays(3));

        Assert.Throws<InvalidOperationException>(() => rental.Complete(Start.AddDays(4)));
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_DoesNotChangeTheReturnDate()
    {
        var rental = StartRental();
        var firstReturn = Start.AddDays(3);
        rental.Complete(firstReturn);

        Assert.Throws<InvalidOperationException>(() => rental.Complete(Start.AddDays(10)));

        Assert.Equal(firstReturn, rental.ActualReturnDate);
    }

    [Fact]
    public void Complete_WithReturnDateBeforeStart_ThrowsArgumentExceptionAndStaysActive()
    {
        var vehicle = TestData.CreateVehicle();
        var rental = StartRental(vehicle);

        Assert.Throws<ArgumentException>(() => rental.Complete(Start.AddDays(-1)));

        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Equal(VehicleAvailabilityStatus.Rented, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void Complete_OnTheStartDate_IsAllowed()
    {
        var rental = StartRental();

        rental.Complete(Start);

        Assert.Equal(RentalStatus.Completed, rental.Status);
    }

    [Fact]
    public void Vehicle_CanBeRentedAgain_AfterRentalIsCompleted()
    {
        var vehicle = TestData.CreateVehicle();
        StartRental(vehicle).Complete(Start.AddDays(3));

        var second = StartRental(vehicle);

        Assert.Equal(RentalStatus.Active, second.Status);
    }
}
