using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Vehicles;

/// <summary>
/// A read-only view of a vehicle, so callers never hold the mutable domain entity.
/// </summary>
public sealed record VehicleDto(
    string Id,
    string Make,
    string Model,
    string DisplayName,
    int Year,
    VehicleType VehicleType,
    decimal DailyRate,
    VehicleAvailabilityStatus AvailabilityStatus);
