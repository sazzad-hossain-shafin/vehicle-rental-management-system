using System.ComponentModel.DataAnnotations;

namespace VehicleRental.Api.Contracts;

/// <summary>
/// Starts a rental today for an existing vehicle and customer. The price is calculated by the system
/// and fixed when the rental starts.
/// </summary>
public sealed class StartRentalApiRequest
{
    /// <summary>The ID of the vehicle to rent.</summary>
    [Required]
    public Guid? VehicleId { get; init; }

    /// <summary>The ID of the customer renting it.</summary>
    [Required]
    public Guid? CustomerId { get; init; }

    /// <summary>How many days to rent for. Must be at least 1.</summary>
    [Required]
    public int? RentalDays { get; init; }

    /// <summary>Whether the customer asked for the promotional discount. It applies only to short rentals.</summary>
    public bool PromotionalDiscountRequested { get; init; }
}
