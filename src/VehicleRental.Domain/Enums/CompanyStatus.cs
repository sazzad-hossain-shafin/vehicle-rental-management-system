namespace VehicleRental.Domain.Enums;

/// <summary>Where a rental company is in its life on the platform. Only <see cref="Active"/> companies are public.</summary>
public enum CompanyStatus
{
    /// <summary>Registered but not yet approved by a platform administrator. Not publicly discoverable.</summary>
    PendingReview,

    /// <summary>Approved and open for business.</summary>
    Active,

    /// <summary>Switched off (rejected, or suspended after approval). Its data is kept.</summary>
    Suspended
}
