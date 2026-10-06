using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;

namespace VehicleRental.Application.Tests;

/// <summary>
/// The use cases behind "customers see only their own rentals".
/// </summary>
public class CustomerOwnershipTests
{
    private readonly TestApp _app = new();

    private async Task<(Guid Alice, Guid Bob, RentalDto AliceRental, RentalDto BobRental)> SetUpAsync()
    {
        var alice = await _app.Customers.CreateAsync("C1", "Alice");
        var bob = await _app.Customers.CreateAsync("C2", "Bob");
        var v1 = await _app.AddVehicleAsync("V1");
        var v2 = await _app.AddVehicleAsync("V2");
        var aliceRental = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(v1.Id, alice.Id, 2));
        var bobRental = await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(v2.Id, bob.Id, 3));

        return (alice.Id, bob.Id, aliceRental, bobRental);
    }

    [Fact]
    public async Task CustomerCanReadTheirOwnRental()
    {
        var (alice, _, aliceRental, _) = await SetUpAsync();

        var found = await _app.Rentals.GetCustomerRentalAsync(aliceRental.Id, alice);

        Assert.Equal(aliceRental.Id, found.Id);
    }

    [Fact]
    public async Task AnotherCustomersRental_IsReportedExactlyLikeAMissingOne()
    {
        var (alice, _, _, bobRental) = await SetUpAsync();

        var notOwned = await Assert.ThrowsAsync<NotFoundException>(
            () => _app.Rentals.GetCustomerRentalAsync(bobRental.Id, alice));
        var missing = await Assert.ThrowsAsync<NotFoundException>(
            () => _app.Rentals.GetCustomerRentalAsync(Guid.NewGuid(), alice));

        // Same exception type and the same message apart from the ID: nothing reveals that the rental exists.
        Assert.Equal(MaskIds(missing.Message), MaskIds(notOwned.Message));
    }

    private static string MaskIds(string message) =>
        System.Text.RegularExpressions.Regex.Replace(
            message,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "<id>");

    [Fact]
    public async Task CustomerHistory_ContainsOnlyTheirOwnRentals_AndCountsOnlyThose()
    {
        var (alice, bob, aliceRental, bobRental) = await SetUpAsync();

        var alicePage = await _app.Rentals.GetCustomerRentalHistoryPageAsync(alice);
        var bobPage = await _app.Rentals.GetCustomerRentalHistoryPageAsync(bob);

        Assert.Equal(new[] { aliceRental.Id }, alicePage.Items.Select(r => r.Id));
        Assert.Equal(1, alicePage.TotalCount);
        Assert.Equal(new[] { bobRental.Id }, bobPage.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task CustomerHistory_ForACustomerWithNoRentals_IsEmpty()
    {
        await SetUpAsync();
        var carol = await _app.Customers.CreateAsync("C3", "Carol");

        var page = await _app.Rentals.GetCustomerRentalHistoryPageAsync(carol.Id);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task CustomerHistory_ForAnUnknownCustomerId_IsEmpty_NotAnError()
    {
        await SetUpAsync();

        var page = await _app.Rentals.GetCustomerRentalHistoryPageAsync(Guid.NewGuid());

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task CustomerHistory_IsPaged()
    {
        var customer = await _app.Customers.CreateAsync("C1", "Alice");
        for (int i = 1; i <= 5; i++)
        {
            var vehicle = await _app.AddVehicleAsync($"V{i}");
            await _app.Rentals.StartRentalAsync(new StartRentalByIdRequest(vehicle.Id, customer.Id, 2));
            _app.Clock.AdvanceDays(1);
        }

        var page = await _app.Rentals.GetCustomerRentalHistoryPageAsync(customer.Id, page: 2, pageSize: 2);

        Assert.Equal(new[] { "V3", "V4" }, page.Items.Select(r => r.VehicleRegistrationNumber));
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, Paging.MaxPageSize + 1)]
    public async Task CustomerHistory_WithOutOfRangePaging_Throws(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _app.Rentals.GetCustomerRentalHistoryPageAsync(Guid.NewGuid(), page, pageSize));
    }
}
