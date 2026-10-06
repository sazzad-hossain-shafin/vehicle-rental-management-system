namespace VehicleRental.Domain.Enums;

/// <summary>
/// The lifecycle state of a single rental.
/// </summary>
public enum RentalStatus
{
    /// <summary>The vehicle is out with the customer.</summary>
    Active,

    /// <summary>The vehicle has been returned.</summary>
    Completed
}
