using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Rentals;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tests;

public class PagingTests
{
    private readonly TestApp _app = new();

    private async Task AddVehiclesAsync(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            await _app.AddVehicleAsync($"V{i:00}", 50m + i);
        }
    }

    // ----- Vehicles -----

    [Fact]
    public async Task VehiclePage_ReturnsTheRequestedSliceAndTheTotal()
    {
        await AddVehiclesAsync(7);

        var page = await _app.Vehicles.SearchPageAsync(new VehicleSearchCriteria(), page: 2, pageSize: 3);

        Assert.Equal(new[] { "V04", "V05", "V06" }, page.Items.Select(v => v.RegistrationNumber));
        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.PageSize);
        Assert.Equal(7, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task VehiclePage_LastPageHoldsTheRemainder()
    {
        await AddVehiclesAsync(7);

        var page = await _app.Vehicles.SearchPageAsync(new VehicleSearchCriteria(), page: 3, pageSize: 3);

        Assert.Equal(new[] { "V07" }, page.Items.Select(v => v.RegistrationNumber));
    }

    [Fact]
    public async Task VehiclePage_BeyondTheLastPage_IsEmptyButKeepsTheTotal()
    {
        await AddVehiclesAsync(3);

        var page = await _app.Vehicles.SearchPageAsync(new VehicleSearchCriteria(), page: 5, pageSize: 10);

        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task VehiclePage_CountsOnlyTheMatchingVehicles()
    {
        await AddVehiclesAsync(10);

        var page = await _app.Vehicles.SearchPageAsync(
            new VehicleSearchCriteria(MaximumDailyRate: 55m), page: 1, pageSize: 2);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(new[] { "V01", "V02" }, page.Items.Select(v => v.RegistrationNumber));
    }

    [Fact]
    public async Task VehiclePage_WithNoVehicles_HasZeroPages()
    {
        var page = await _app.Vehicles.SearchPageAsync(new VehicleSearchCriteria());

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public async Task VehiclePage_UsesTheDefaultPageSize_WhenNoneIsGiven()
    {
        await AddVehiclesAsync(Paging.DefaultPageSize + 5);

        var page = await _app.Vehicles.SearchPageAsync(new VehicleSearchCriteria());

        Assert.Equal(Paging.DefaultPageSize, page.Items.Count);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, Paging.MaxPageSize + 1)]
    [InlineData(int.MaxValue, 100)]
    public async Task VehiclePage_WithOutOfRangePaging_ThrowsArgumentOutOfRangeException(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _app.Vehicles.SearchPageAsync(new VehicleSearchCriteria(), page, pageSize));
    }

    [Fact]
    public async Task VehiclePage_AcceptsTheMaximumPageSize()
    {
        var page = await _app.Vehicles.SearchPageAsync(new VehicleSearchCriteria(), 1, Paging.MaxPageSize);

        Assert.Equal(Paging.MaxPageSize, page.PageSize);
    }

    // ----- Rental history -----

    private async Task StartRentalsAsync(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            var vehicle = await _app.AddVehicleAsync($"R{i:00}");
            var customer = await _app.Customers.RegisterOrGetAsync($"C{i}", $"Person {i}");
            await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, customer.Id, 2));
            _app.Clock.AdvanceDays(1); // distinct start dates give a defined order
        }
    }

    [Fact]
    public async Task RentalHistoryPage_ReturnsTheRequestedSliceOldestFirst()
    {
        await StartRentalsAsync(5);

        var page = await _app.Rentals.GetRentalHistoryPageAsync(page: 2, pageSize: 2);

        Assert.Equal(new[] { "R03", "R04" }, page.Items.Select(r => r.VehicleRegistrationNumber));
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task RentalHistoryPage_WithNoRentals_IsEmpty()
    {
        var page = await _app.Rentals.GetRentalHistoryPageAsync();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, Paging.MaxPageSize + 1)]
    public async Task RentalHistoryPage_WithOutOfRangePaging_ThrowsArgumentOutOfRangeException(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _app.Rentals.GetRentalHistoryPageAsync(page, pageSize));
    }

    [Fact]
    public async Task RentalHistoryPage_IncludesCompletedAndActiveRentals()
    {
        await StartRentalsAsync(2);
        await _app.Rentals.ReturnVehicleAsync("R01");

        var page = await _app.Rentals.GetRentalHistoryPageAsync();

        Assert.Equal(new[] { RentalStatus.Completed, RentalStatus.Active }, page.Items.Select(r => r.Status));
    }
}
