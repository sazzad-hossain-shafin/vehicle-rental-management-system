namespace VehicleRental.Application.Rentals;

/// <summary>
/// Starts a rental today for the given number of days.
/// </summary>
/// <param name="VehicleId">The vehicle to rent.</param>
/// <param name="CustomerId">The customer's ID. An unknown ID registers a new customer.</param>
/// <param name="CustomerName">The customer's name, used when registering and checked against an existing customer.</param>
/// <param name="RentalDays">The number of days to rent for.</param>
/// <param name="PromotionalDiscountRequested">Whether the customer asked for the promotion; it applies only where the pricing policy allows.</param>
public sealed record StartRentalRequest(
    string VehicleId,
    string CustomerId,
    string CustomerName,
    int RentalDays,
    bool PromotionalDiscountRequested = false);
