namespace VehicleRental.Application.Rentals;

/// <summary>
/// Starts a rental today for the given number of days.
/// </summary>
/// <param name="VehicleRegistrationNumber">The registration number of the vehicle to rent.</param>
/// <param name="CustomerNumber">The customer's number. An unknown number registers a new customer.</param>
/// <param name="CustomerName">The customer's name, used when registering and checked against an existing customer.</param>
/// <param name="RentalDays">The number of days to rent for.</param>
/// <param name="PromotionalDiscountRequested">Whether the customer asked for the promotion; it applies only where the pricing policy allows.</param>
public sealed record StartRentalRequest(
    string VehicleRegistrationNumber,
    string CustomerNumber,
    string CustomerName,
    int RentalDays,
    bool PromotionalDiscountRequested = false);
