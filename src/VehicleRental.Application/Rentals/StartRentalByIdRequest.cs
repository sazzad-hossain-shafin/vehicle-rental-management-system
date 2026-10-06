namespace VehicleRental.Application.Rentals;

/// <summary>
/// Starts a rental today for an existing vehicle and an existing customer, both identified by their
/// permanent IDs. This is the form clients such as the web API use; <see cref="StartRentalRequest"/>
/// is the form that identifies them by business number and can register a new customer.
/// </summary>
/// <param name="VehicleId">The vehicle to rent.</param>
/// <param name="CustomerId">The customer renting it.</param>
/// <param name="RentalDays">The number of days to rent for.</param>
/// <param name="PromotionalDiscountRequested">Whether the customer asked for the promotion; it applies only where the pricing policy allows.</param>
public sealed record StartRentalByIdRequest(
    Guid VehicleId,
    Guid CustomerId,
    int RentalDays,
    bool PromotionalDiscountRequested = false);
