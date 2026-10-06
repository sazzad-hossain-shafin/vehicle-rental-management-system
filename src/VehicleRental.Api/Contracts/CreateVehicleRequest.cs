using System.ComponentModel.DataAnnotations;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Api.Contracts;

/// <summary>
/// The business data for a new vehicle. The ID and the availability are set by the system, not the client.
/// </summary>
public sealed class CreateVehicleRequest
{
    /// <summary>The vehicle's registration (plate) number, for example "ABC-123". Unique across the fleet.</summary>
    [Required]
    public string? RegistrationNumber { get; init; }

    [Required]
    public string? Make { get; init; }

    [Required]
    public string? Model { get; init; }

    [Required]
    public int? Year { get; init; }

    /// <summary>Car, Motorcycle or Van.</summary>
    [Required]
    public VehicleType? VehicleType { get; init; }

    /// <summary>The price per day. Must be greater than zero.</summary>
    [Required]
    public decimal? DailyRate { get; init; }
}
