using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.ConsoleApp;

/// <summary>
/// Demo fleet for the console client, with fictional registration numbers. It is added through
/// the same use case a user would use, and is not part of the domain.
/// </summary>
internal static class SampleData
{
    /// <summary>
    /// Adds the demo vehicles, but only to an empty fleet, so restarting the app never duplicates them
    /// and never touches a fleet that already has data.
    /// </summary>
    /// <returns>True if the vehicles were added.</returns>
    public static async Task<bool> SeedVehiclesAsync(VehicleService vehicles)
    {
        if ((await vehicles.GetAllAsync()).Count > 0)
        {
            return false;
        }

        await vehicles.AddVehicleAsync(new AddVehicleRequest("ABC-123", "Toyota", "Corolla", 2022, VehicleType.Car, 60m));
        await vehicles.AddVehicleAsync(new AddVehicleRequest("DEF-456", "Honda", "CB500", 2021, VehicleType.Motorcycle, 40m));
        await vehicles.AddVehicleAsync(new AddVehicleRequest("GHI-789", "Toyota", "HiAce", 2021, VehicleType.Van, 90m));

        return true;
    }
}
