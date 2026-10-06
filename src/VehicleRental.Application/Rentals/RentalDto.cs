using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Rentals;

/// <summary>
/// A read-only view of a rental, including its stored price snapshot.
/// </summary>
public sealed record RentalDto(
    Guid Id,
    string CustomerId,
    string CustomerName,
    string VehicleId,
    string VehicleDisplayName,
    VehicleType VehicleType,
    DateOnly StartDate,
    DateOnly ExpectedReturnDate,
    DateOnly? ActualReturnDate,
    RentalStatus Status,
    decimal DailyRateAtRental,
    int BillableDays,
    string PricingDescription,
    decimal TotalCost);
