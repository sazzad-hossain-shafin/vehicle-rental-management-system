using VehicleRental.Domain.Entities;

namespace VehicleRental.Domain.Tests;

public class CustomerTests
{
    [Fact]
    public void Create_WithValidDetails_KeepsIdAndName()
    {
        var customer = new Customer("C1", "Alice");

        Assert.Equal("C1", customer.CustomerNumber);
        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Equal("Alice", customer.Name);
    }

    [Fact]
    public void Create_WithSurroundingWhitespace_TrimsIdAndName()
    {
        var customer = new Customer("  C1 ", " Alice  ");

        Assert.Equal("C1", customer.CustomerNumber);
        Assert.Equal("Alice", customer.Name);
    }

    [Fact]
    public void Create_NormalizesCustomerNumberToUpperCase()
    {
        var customer = new Customer("c-100", "Alice");

        Assert.Equal("C-100", customer.CustomerNumber);
    }

    [Fact]
    public void Create_GeneratesADifferentIdentifierForEachCustomer()
    {
        Assert.NotEqual(new Customer("C1", "Alice").Id, new Customer("C2", "Bob").Id);
    }

    [Fact]
    public void Create_WithCustomerNumberOrNameTooLong_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new Customer(new string('1', Customer.CustomerNumberMaxLength + 1), "Alice"));
        Assert.Throws<ArgumentException>(
            () => new Customer("C1", new string('x', Customer.NameMaxLength + 1)));
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
