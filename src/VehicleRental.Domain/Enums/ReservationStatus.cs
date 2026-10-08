namespace VehicleRental.Domain.Enums;

public enum ReservationStatus
{
    /// <summary>A booking that holds the vehicle for its dates.</summary>
    Active,

    /// <summary>Cancelled before pickup. It no longer holds the vehicle.</summary>
    Cancelled,

    /// <summary>Picked up: the vehicle was handed over and a rental was started from it.</summary>
    Fulfilled
}
