using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests;

public class RentalServiceTests
{
    private readonly TestApp _app = new();

    // ----- Starting a rental -----

    [Fact]
    public async Task StartRental_WithValidRequest_ReturnsActiveRentalWithAgreedPrice()
    {
        await _app.AddVehicleAsync("V1", dailyRate: 100m);

        var rental = await _app.StartRentalAsync(days: 3);

        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Equal("V1", rental.VehicleRegistrationNumber);
        Assert.Equal("C1", rental.CustomerNumber);
        Assert.Equal(TestApp.Today, rental.StartDate);
        Assert.Equal(TestApp.Today.AddDays(3), rental.ExpectedReturnDate);
        Assert.Equal(3, rental.BillableDays);
        Assert.Equal(100m, rental.DailyRateAtRental);
        Assert.Equal(300m, rental.TotalCost);
    }

    [Fact]
    public async Task StartRental_FindsTheVehicleByRegistrationNumberInAnyCase()
    {
        await _app.AddVehicleAsync("ABC-123");

        var rental = await _app.StartRentalAsync(vehicleId: " abc-123 ");

        Assert.Equal("ABC-123", rental.VehicleRegistrationNumber);
    }

    [Fact]
    public async Task StartRental_StoresTheActiveRentalAndMarksTheVehicleRented()
    {
        await _app.AddVehicleAsync("V1");

        var rental = await _app.StartRentalAsync();

        Assert.Equal(1, _app.RentalRepository.Count);
        var active = await _app.Rentals.GetActiveRentalForVehicleAsync("V1");
        Assert.Equal(rental.Id, active?.Id);
        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("V1");
        Assert.Equal(VehicleAvailabilityStatus.Rented, vehicle.AvailabilityStatus);
    }

    [Fact]
    public async Task StartRental_WithUnknownVehicle_ThrowsNotFoundExceptionAndStoresNothing()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.StartRentalAsync(vehicleId: "missing"));

        Assert.Equal(0, _app.RentalRepository.Count);
        Assert.Equal(0, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task StartRental_WhenVehicleIsAlreadyRented_ThrowsConflictException()
    {
        await _app.AddVehicleAsync("V1");
        await _app.StartRentalAsync(customerId: "C1", customerName: "Alice");

        await Assert.ThrowsAsync<ConflictException>(
            () => _app.StartRentalAsync(customerId: "C2", customerName: "Bob"));

        Assert.Equal(1, _app.RentalRepository.Count);
    }

    [Fact]
    public async Task StartRental_WhenVehicleIsAlreadyRented_DoesNotRegisterTheSecondCustomer()
    {
        await _app.AddVehicleAsync("V1");
        await _app.StartRentalAsync(customerId: "C1", customerName: "Alice");

        await Assert.ThrowsAsync<ConflictException>(
            () => _app.StartRentalAsync(customerId: "C2", customerName: "Bob"));

        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task StartRental_ForTwoVehicles_AllowsBothToBeActiveAtOnce()
    {
        await _app.AddVehicleAsync("V1");
        await _app.AddVehicleAsync("V2");

        await _app.StartRentalAsync(vehicleId: "V1", customerId: "C1", customerName: "Alice");
        await _app.StartRentalAsync(vehicleId: "V2", customerId: "C2", customerName: "Bob");

        Assert.Equal(2, _app.RentalRepository.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task StartRental_WithNonPositiveDays_ThrowsArgumentExceptionAndLeavesVehicleAvailable(int days)
    {
        await _app.AddVehicleAsync("V1");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _app.StartRentalAsync(days: days));

        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("V1");
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
        Assert.Equal(0, _app.RentalRepository.Count);
        Assert.Equal(0, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task StartRental_WithEmptyCustomerName_ThrowsArgumentExceptionAndLeavesVehicleAvailable()
    {
        await _app.AddVehicleAsync("V1");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _app.StartRentalAsync(customerName: " "));

        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("V1");
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    // ----- Customers -----

    [Fact]
    public async Task StartRental_WithNewCustomer_RegistersTheCustomer()
    {
        await _app.AddVehicleAsync("V1");

        await _app.StartRentalAsync(customerId: "C9", customerName: "Dana");

        var customer = await _app.Customers.GetByCustomerNumberAsync("C9");
        Assert.Equal("Dana", customer.Name);
        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task StartRental_WithExistingCustomer_ReusesThemInsteadOfCreatingAnother()
    {
        await _app.AddVehicleAsync("V1");
        await _app.AddVehicleAsync("V2");

        await _app.StartRentalAsync(vehicleId: "V1", customerId: "C1", customerName: "Alice");
        await _app.StartRentalAsync(vehicleId: "V2", customerId: "C1", customerName: "Alice");

        Assert.Equal(1, _app.CustomerRepository.Count);
        Assert.Equal(2, _app.RentalRepository.Count);
    }

    [Fact]
    public async Task StartRental_WithKnownCustomerIdButDifferentName_ThrowsConflictExceptionAndLeavesVehicleAvailable()
    {
        await _app.AddVehicleAsync("V1");
        await _app.AddVehicleAsync("V2");
        await _app.StartRentalAsync(vehicleId: "V1", customerId: "C1", customerName: "Alice");

        await Assert.ThrowsAsync<ConflictException>(
            () => _app.StartRentalAsync(vehicleId: "V2", customerId: "C1", customerName: "Mallory"));

        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("V2");
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
        Assert.Equal("Alice", (await _app.Customers.GetByCustomerNumberAsync("C1")).Name);
    }

    // ----- Pricing path (rate 100/day) -----

    [Theory]
    [InlineData(3, false, 300, "Normal")]
    [InlineData(3, true, 270, "Promotional")]
    [InlineData(6, true, 540, "Promotional")]
    [InlineData(6, false, 600, "Normal")]
    [InlineData(7, false, 560, "Long-term")]
    [InlineData(7, true, 560, "Long-term")]
    [InlineData(10, true, 800, "Long-term")]
    public async Task StartRental_AppliesThePricingPolicy(
        int days, bool promotion, int expectedTotal, string expectedPricingPrefix)
    {
        await _app.AddVehicleAsync("V1", dailyRate: 100m);

        var rental = await _app.StartRentalAsync(days: days, promotion: promotion);

        Assert.Equal((decimal)expectedTotal, rental.TotalCost);
        Assert.StartsWith(expectedPricingPrefix, rental.PricingDescription);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void IsPromotionalDiscountAvailable_FollowsThePricingPolicy(int days, bool expected)
    {
        Assert.Equal(expected, _app.Rentals.IsPromotionalDiscountAvailable(days));
    }

    // ----- Returning -----

    [Fact]
    public async Task ReturnVehicle_WithActiveRental_CompletesItAndMakesTheVehicleAvailable()
    {
        await _app.AddVehicleAsync("V1");
        await _app.StartRentalAsync(days: 3);
        _app.Clock.AdvanceDays(2);

        var completed = await _app.Rentals.ReturnVehicleAsync("V1");

        Assert.Equal(RentalStatus.Completed, completed.Status);
        Assert.Equal(TestApp.Today.AddDays(2), completed.ActualReturnDate);
        var vehicle = await _app.Vehicles.GetByRegistrationNumberAsync("V1");
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
        Assert.Null(await _app.Rentals.GetActiveRentalForVehicleAsync("V1"));
    }

    [Fact]
    public async Task ReturnVehicle_WhenVehicleIsNotRented_ThrowsConflictException()
    {
        await _app.AddVehicleAsync("V1");

        await Assert.ThrowsAsync<ConflictException>(() => _app.Rentals.ReturnVehicleAsync("V1"));
    }

    [Fact]
    public async Task ReturnVehicle_WithUnknownVehicle_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Rentals.ReturnVehicleAsync("missing"));
    }

    [Fact]
    public async Task ReturnVehicle_Twice_FailsTheSecondTime()
    {
        await _app.AddVehicleAsync("V1");
        await _app.StartRentalAsync();
        await _app.Rentals.ReturnVehicleAsync("V1");

        await Assert.ThrowsAsync<ConflictException>(() => _app.Rentals.ReturnVehicleAsync("V1"));
    }

    [Fact]
    public async Task ReturnVehicle_KeepsTheCompletedRentalInHistory()
    {
        await _app.AddVehicleAsync("V1");
        await _app.StartRentalAsync();
        await _app.Rentals.ReturnVehicleAsync("V1");

        var history = await _app.Rentals.GetRentalHistoryAsync();

        var only = Assert.Single(history);
        Assert.Equal(RentalStatus.Completed, only.Status);
    }

    // ----- History -----

    [Fact]
    public async Task GetRentalHistory_WithNoRentals_ReturnsEmptyList()
    {
        Assert.Empty(await _app.Rentals.GetRentalHistoryAsync());
    }

    [Fact]
    public async Task GetRentalHistory_ListsActiveAndCompletedRentalsOldestFirst()
    {
        await _app.AddVehicleAsync("V1");
        await _app.AddVehicleAsync("V2");
        await _app.StartRentalAsync(vehicleId: "V1", customerId: "C1", customerName: "Alice");
        _app.Clock.AdvanceDays(1);
        await _app.StartRentalAsync(vehicleId: "V2", customerId: "C2", customerName: "Bob");
        await _app.Rentals.ReturnVehicleAsync("V1");

        var history = await _app.Rentals.GetRentalHistoryAsync();

        Assert.Equal(new[] { "V1", "V2" }, history.Select(r => r.VehicleRegistrationNumber));
        Assert.Equal(new[] { RentalStatus.Completed, RentalStatus.Active }, history.Select(r => r.Status));
    }

    [Fact]
    public async Task HistoricalTotal_DoesNotChange_WhenVehicleIsRentedAgainUnderDifferentPricing()
    {
        await _app.AddVehicleAsync("V1", dailyRate: 100m);
        var first = await _app.StartRentalAsync(customerId: "C1", customerName: "Alice", days: 5);
        await _app.Rentals.ReturnVehicleAsync("V1");
        _app.Clock.AdvanceDays(5);

        var second = await _app.StartRentalAsync(customerId: "C2", customerName: "Bob", days: 10);

        var history = await _app.Rentals.GetRentalHistoryAsync();
        Assert.Equal(500m, first.TotalCost);
        Assert.Equal(800m, second.TotalCost);
        Assert.Equal(500m, history.Single(r => r.Id == first.Id).TotalCost);
        Assert.Equal(800m, history.Single(r => r.Id == second.Id).TotalCost);
    }

    // ----- Active rental lookup -----

    [Fact]
    public async Task GetActiveRentalForVehicle_WhenNotRented_ReturnsNull()
    {
        await _app.AddVehicleAsync("V1");

        Assert.Null(await _app.Rentals.GetActiveRentalForVehicleAsync("V1"));
    }

    [Fact]
    public async Task GetActiveRentalForVehicle_WithUnknownVehicle_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _app.Rentals.GetActiveRentalForVehicleAsync("missing"));
    }

    // ----- Cancellation -----

    [Fact]
    public async Task StartRental_WithCancelledToken_ThrowsOperationCanceledExceptionAndStoresNothing()
    {
        await _app.AddVehicleAsync("V1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _app.Rentals.StartRentalAsync(
                new(VehicleRegistrationNumber: "V1", CustomerNumber: "C1", CustomerName: "Alice", RentalDays: 3),
                cts.Token));

        Assert.Equal(0, _app.RentalRepository.Count);
    }
}
