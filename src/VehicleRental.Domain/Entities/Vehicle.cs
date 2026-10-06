using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Entities;

/// <summary>
/// A vehicle in the rental fleet. Its descriptive details are fixed at creation;
/// only its availability changes, and only through <see cref="MarkAsRented"/>
/// and <see cref="MarkAsAvailable"/>.
/// </summary>
public class Vehicle
{
    public const int MinimumYear = 1900;

    public string Id { get; }
    public string Make { get; }
    public string Model { get; }
    public int Year { get; }
    public VehicleType VehicleType { get; }
    public decimal DailyRate { get; }
    public VehicleAvailabilityStatus AvailabilityStatus { get; private set; }

    /// <summary>Make and model, for example "Toyota Corolla".</summary>
    public string DisplayName => $"{Make} {Model}";

    /// <summary>
    /// Creates an available vehicle.
    /// </summary>
    /// <exception cref="ArgumentException">The ID, make or model is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The year is outside the supported range, the vehicle type is undefined,
    /// or the daily rate is not greater than zero.
    /// </exception>
    public Vehicle(
        string id,
        string make,
        string model,
        int year,
        VehicleType vehicleType,
        decimal dailyRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, MinimumYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, DateTime.UtcNow.Year + 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dailyRate, 0m);

        if (!Enum.IsDefined(vehicleType))
        {
            throw new ArgumentOutOfRangeException(nameof(vehicleType));
        }

        Id = id.Trim();
        Make = make.Trim();
        Model = model.Trim();
        Year = year;
        VehicleType = vehicleType;
        DailyRate = dailyRate;
        AvailabilityStatus = VehicleAvailabilityStatus.Available;
    }

    /// <exception cref="InvalidOperationException">The vehicle is already rented.</exception>
    public void MarkAsRented()
    {
        if (AvailabilityStatus != VehicleAvailabilityStatus.Available)
        {
            throw new InvalidOperationException($"Vehicle '{Id}' is not available.");
        }

        AvailabilityStatus = VehicleAvailabilityStatus.Rented;
    }

    /// <exception cref="InvalidOperationException">The vehicle is not currently rented.</exception>
    public void MarkAsAvailable()
    {
        if (AvailabilityStatus != VehicleAvailabilityStatus.Rented)
        {
            throw new InvalidOperationException($"Vehicle '{Id}' is not currently rented.");
        }

        AvailabilityStatus = VehicleAvailabilityStatus.Available;
    }
}
