using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Tests;

public class VehicleTests
{
    [Fact]
    public void Create_WithValidDetails_IsAvailableAndKeepsDetails()
    {
        var vehicle = new Vehicle("V1", "Honda", "CB500", 2021, VehicleType.Motorcycle, 40m);

        Assert.Equal("V1", vehicle.RegistrationNumber);
        Assert.NotEqual(Guid.Empty, vehicle.Id);
        Assert.Equal("Honda CB500", vehicle.DisplayName);
        Assert.Equal(VehicleType.Motorcycle, vehicle.VehicleType);
        Assert.Equal(40m, vehicle.DailyRate);
        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void Create_GeneratesADifferentIdentifierForEachVehicle()
    {
        var first = TestData.CreateVehicle("V1");
        var second = TestData.CreateVehicle("V2");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("  abc-123 ", "ABC-123")]
    [InlineData("xyz 789", "XYZ 789")]
    [InlineData("v1", "V1")]
    public void Create_NormalizesRegistrationNumberToTrimmedUpperCase(string input, string expected)
    {
        var vehicle = new Vehicle(input, "Toyota", "Corolla", 2022, VehicleType.Car, 60m);

        Assert.Equal(expected, vehicle.RegistrationNumber);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("-AB")]
    [InlineData("AB-")]
    [InlineData("AB_123")]
    [InlineData("AB@123")]
    [InlineData("ABCDEFGHIJKLM")]
    public void Create_WithInvalidRegistrationNumber_ThrowsArgumentException(string registration)
    {
        Assert.Throws<ArgumentException>(
            () => new Vehicle(registration, "Toyota", "Corolla", 2022, VehicleType.Car, 60m));
    }

    [Fact]
    public void NormalizeRegistrationNumber_HandlesNullAndWhitespace()
    {
        Assert.Equal("", Vehicle.NormalizeRegistrationNumber(null));
        Assert.Equal("", Vehicle.NormalizeRegistrationNumber("   "));
        Assert.Equal("ABC-123", Vehicle.NormalizeRegistrationNumber(" abc-123 "));
    }

    [Fact]
    public void Create_WithMakeOrModelTooLong_ThrowsArgumentException()
    {
        var tooLong = new string('x', Vehicle.MakeMaxLength + 1);

        Assert.Throws<ArgumentException>(
            () => new Vehicle("V1", tooLong, "Corolla", 2022, VehicleType.Car, 60m));
        Assert.Throws<ArgumentException>(
            () => new Vehicle("V1", "Toyota", tooLong, 2022, VehicleType.Car, 60m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyId_ThrowsArgumentException(string id)
    {
        Assert.Throws<ArgumentException>(
            () => new Vehicle(id, "Toyota", "Corolla", 2022, VehicleType.Car, 60m));
    }

    [Fact]
    public void Create_WithNullId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Vehicle(null!, "Toyota", "Corolla", 2022, VehicleType.Car, 60m));
    }

    [Theory]
    [InlineData("", "Corolla")]
    [InlineData("Toyota", "")]
    [InlineData("  ", "Corolla")]
    [InlineData("Toyota", "  ")]
    public void Create_WithEmptyMakeOrModel_ThrowsArgumentException(string make, string model)
    {
        Assert.Throws<ArgumentException>(
            () => new Vehicle("V1", make, model, 2022, VehicleType.Car, 60m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50.5)]
    public void Create_WithNonPositiveDailyRate_ThrowsArgumentOutOfRangeException(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Vehicle("V1", "Toyota", "Corolla", 2022, VehicleType.Car, (decimal)rate));
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(0)]
    [InlineData(9999)]
    public void Create_WithUnreasonableYear_ThrowsArgumentOutOfRangeException(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Vehicle("V1", "Toyota", "Corolla", year, VehicleType.Car, 60m));
    }

    [Fact]
    public void Create_WithUndefinedVehicleType_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Vehicle("V1", "Toyota", "Corolla", 2022, (VehicleType)99, 60m));
    }

    [Fact]
    public void MarkAsRented_WhenAvailable_BecomesRented()
    {
        var vehicle = TestData.CreateVehicle();

        vehicle.MarkAsRented();

        Assert.Equal(VehicleAvailabilityStatus.Rented, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void MarkAsRented_WhenAlreadyRented_ThrowsInvalidOperationException()
    {
        var vehicle = TestData.CreateVehicle();
        vehicle.MarkAsRented();

        Assert.Throws<InvalidOperationException>(() => vehicle.MarkAsRented());
    }

    [Fact]
    public void MarkAsAvailable_WhenRented_BecomesAvailable()
    {
        var vehicle = TestData.CreateVehicle();
        vehicle.MarkAsRented();

        vehicle.MarkAsAvailable();

        Assert.Equal(VehicleAvailabilityStatus.Available, vehicle.AvailabilityStatus);
    }

    [Fact]
    public void MarkAsAvailable_WhenAlreadyAvailable_ThrowsInvalidOperationException()
    {
        var vehicle = TestData.CreateVehicle();

        Assert.Throws<InvalidOperationException>(() => vehicle.MarkAsAvailable());
    }
}
