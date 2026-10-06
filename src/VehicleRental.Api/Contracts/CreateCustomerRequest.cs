using System.ComponentModel.DataAnnotations;

namespace VehicleRental.Api.Contracts;

/// <summary>
/// The business data for a new customer. The ID is set by the system, not the client.
/// </summary>
public sealed class CreateCustomerRequest
{
    /// <summary>The customer's number, for example "C-1001". Unique across customers.</summary>
    [Required]
    public string? CustomerNumber { get; init; }

    [Required]
    public string? Name { get; init; }
}
