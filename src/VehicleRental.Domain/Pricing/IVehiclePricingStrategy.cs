namespace VehicleRental.Domain.Pricing;

/// <summary>
/// Calculates what a rental costs. Strategies are stateless and are used once,
/// when a rental is created; the result is stored on the rental.
/// </summary>
public interface IVehiclePricingStrategy
{
    /// <summary>Human-readable name, stored on the rental as part of its price record.</summary>
    string Name { get; }

    /// <param name="dailyRate">The vehicle's daily rate; must be greater than zero.</param>
    /// <param name="billableDays">The number of billable days; must be at least one.</param>
    /// <returns>The total cost, rounded to two decimal places.</returns>
    decimal CalculateCost(decimal dailyRate, int billableDays);
}
