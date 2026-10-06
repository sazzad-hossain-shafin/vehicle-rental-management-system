using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.ConsoleApp;

/// <summary>
/// Demo fleet for the console client. It is added through the same use case a user
/// would use, and is not part of the domain.
/// </summary>
internal static class SampleData
{
    public static async Task SeedVehiclesAsync(VehicleService vehicles)
    {
        await vehicles.AddVehicleAsync(new AddVehicleRequest("1", "Toyota", "Corolla", 2022, VehicleType.Car, 60m));
        await vehicles.AddVehicleAsync(new AddVehicleRequest("2", "Honda", "CB500", 2021, VehicleType.Motorcycle, 40m));
        await vehicles.AddVehicleAsync(new AddVehicleRequest("3", "Toyota", "HiAce", 2021, VehicleType.Van, 90m));
    }
}
