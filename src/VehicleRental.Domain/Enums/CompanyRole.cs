namespace VehicleRental.Domain.Enums;

/// <summary>What a user may do inside one company.</summary>
public enum CompanyRole
{
    /// <summary>Everything in the company, including staff, settings, fleet and reports.</summary>
    Owner,

    /// <summary>Bookings, customer assistance, pickup and return only.</summary>
    Staff
}
