using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Tests;

internal static class TestData
{
    public static readonly DateOnly Oct1 = new(2026, 10, 1);

    public static Vehicle CreateVehicle(
        string id = "V1",
        decimal dailyRate = 100m,
        VehicleType type = VehicleType.Car) =>
        new(id, "Toyota", "Corolla", 2022, type, dailyRate);

    public static Customer CreateCustomer(string id = "C1", string name = "Alice") =>
        new(id, name);
}
