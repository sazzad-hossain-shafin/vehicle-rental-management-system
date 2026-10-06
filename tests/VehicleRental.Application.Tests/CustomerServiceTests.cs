using VehicleRental.Application.Exceptions;

namespace VehicleRental.Application.Tests;

public class CustomerServiceTests
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task RegisterOrGet_WithNewId_CreatesAndStoresCustomer()
    {
        var customer = await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        Assert.Equal("C1", customer.Id);
        Assert.Equal("Alice", customer.Name);
        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task RegisterOrGet_WithExistingIdAndSameName_ReusesCustomer()
    {
        await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        var again = await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        Assert.Equal("Alice", again.Name);
        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task RegisterOrGet_RepeatedManyTimes_NeverCreatesDuplicateRecords()
    {
        for (int i = 0; i < 5; i++)
        {
            await _app.Customers.RegisterOrGetAsync("C1", "Alice");
        }

        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task RegisterOrGet_WithExistingIdAndDifferentName_ThrowsConflictException()
    {
        await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        await Assert.ThrowsAsync<ConflictException>(
            () => _app.Customers.RegisterOrGetAsync("C1", "Bob"));
    }

    [Fact]
    public async Task RegisterOrGet_WithConflictingName_DoesNotOverwriteTheStoredName()
    {
        await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        await Assert.ThrowsAsync<ConflictException>(
            () => _app.Customers.RegisterOrGetAsync("C1", "Bob"));

        var stored = await _app.Customers.GetByIdAsync("C1");
        Assert.Equal("Alice", stored.Name);
        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("ALICE")]
    [InlineData("  Alice  ")]
    public async Task RegisterOrGet_WithSameNameInDifferentCaseOrSpacing_ReusesCustomer(string name)
    {
        await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        var again = await _app.Customers.RegisterOrGetAsync("C1", name);

        Assert.Equal("Alice", again.Name);
        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task RegisterOrGet_WithIdInDifferentCase_ReusesCustomer()
    {
        await _app.Customers.RegisterOrGetAsync("c1", "Alice");

        await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        Assert.Equal(1, _app.CustomerRepository.Count);
    }

    [Theory]
    [InlineData("", "Alice")]
    [InlineData("C1", "")]
    [InlineData("C1", "   ")]
    public async Task RegisterOrGet_WithEmptyDetails_ThrowsArgumentExceptionAndStoresNothing(
        string id, string name)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _app.Customers.RegisterOrGetAsync(id, name));

        Assert.Equal(0, _app.CustomerRepository.Count);
    }

    [Fact]
    public async Task GetById_WithExistingId_ReturnsCustomer()
    {
        await _app.Customers.RegisterOrGetAsync("C1", "Alice");

        var customer = await _app.Customers.GetByIdAsync("C1");

        Assert.Equal("Alice", customer.Name);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _app.Customers.GetByIdAsync("missing"));
    }
}
