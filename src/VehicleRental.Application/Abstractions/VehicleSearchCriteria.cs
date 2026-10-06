using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Abstractions;

/// <summary>
/// Optional filters for finding vehicles. A null filter matches everything.
/// </summary>
/// <param name="VehicleType">Only vehicles of this type.</param>
/// <param name="MaximumDailyRate">Only vehicles whose daily rate is at most this amount.</param>
/// <param name="Availability">Only vehicles with this availability.</param>
public sealed record VehicleSearchCriteria(
    VehicleType? VehicleType = null,
    decimal? MaximumDailyRate = null,
    VehicleAvailabilityStatus? Availability = null);
