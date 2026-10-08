using VehicleRental.Application.Rentals;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Reservations;

/// <summary>
/// A read-only view of a reservation, including its stored price quote. The period is the half-open
/// interval <c>[StartDate, EndDate)</c>: the vehicle is held on the start date but not on the end date.
/// <c>IsExpired</c> marks an active reservation whose period is over without a pickup (a no-show).
/// </summary>
public sealed record ReservationDto(
    Guid Id,
    Guid CustomerId,
    string CustomerNumber,
    string CustomerName,
    Guid VehicleId,
    string VehicleRegistrationNumber,
    string VehicleDisplayName,
    VehicleType VehicleType,
    DateOnly StartDate,
    DateOnly EndDate,
    ReservationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? FulfilledAt,
    Guid? RentalId,
    decimal DailyRateAtReservation,
    int BillableDays,
    string PricingDescription,
    decimal TotalCost,
    bool IsExpired);

/// <summary>The result of picking a reservation up: the fulfilled reservation and the rental it started.</summary>
public sealed record PickupResultDto(ReservationDto Reservation, RentalDto Rental);
