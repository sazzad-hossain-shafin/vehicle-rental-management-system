using VehicleRental.Domain.Entities;

namespace VehicleRental.Domain.Tests;

public class CustomerTests
{
    [Fact]
    public void Create_WithValidDetails_KeepsIdAndName()
    {
        var customer = new Customer("C1", "Alice");

        Assert.Equal("C1", customer.Id);
        Assert.Equal("Alice", customer.Name);
    }

    [Fact]
    public void Create_WithSurroundingWhitespace_TrimsIdAndName()
    {
        var customer = new Customer("  C1 ", " Alice  ");

        Assert.Equal("C1", customer.Id);
        Assert.Equal("Alice", customer.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyId_ThrowsArgumentException(string id)
    {
        Assert.Throws<ArgumentException>(() => new Customer(id, "Alice"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() => new Customer("C1", name));
    }

    [Fact]
    public void Create_WithNullName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Customer("C1", null!));
    }
}
