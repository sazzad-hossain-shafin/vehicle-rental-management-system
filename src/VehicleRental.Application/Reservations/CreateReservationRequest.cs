namespace VehicleRental.Application.Reservations;

/// <summary>
/// Reserves a vehicle for a customer. The customer is identified by ID; for a customer's own booking the
/// caller must take that ID from the signed-in account, never from client input.
/// </summary>
/// <param name="StartDate">The first day the vehicle is held.</param>
/// <param name="EndDate">The day the vehicle is free again (not held; the interval is half-open).</param>
/// <param name="PromotionalDiscountRequested">Whether the promotional discount applies to a short booking.</param>
public sealed record CreateReservationRequest(
    Guid CustomerId,
    Guid VehicleId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool PromotionalDiscountRequested = false);
