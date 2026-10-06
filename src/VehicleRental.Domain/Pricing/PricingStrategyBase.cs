using System.Globalization;

namespace VehicleRental.Domain.Pricing;

/// <summary>
/// Shared calculation for strategies that are "daily rate x days, less a fixed
/// percentage". Subclasses only supply their name and discount rate.
/// </summary>
public abstract class PricingStrategyBase : IVehiclePricingStrategy
{
    public abstract string Name { get; }

    /// <summary>The fraction taken off the standard cost, for example 0.10 for 10%.</summary>
    protected abstract decimal DiscountRate { get; }

    /// <summary>
    /// The discount as text, for example "10%". Formatted with the invariant culture because the name is
    /// stored on the rental, so it must not depend on the culture of the machine that happens to run the code.
    /// </summary>
    protected string DiscountPercentText =>
        (DiscountRate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public decimal CalculateCost(decimal dailyRate, int billableDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dailyRate, 0m);
        ArgumentOutOfRangeException.ThrowIfLessThan(billableDays, 1);

        decimal standardCost = dailyRate * billableDays;
        decimal discountedCost = standardCost * (1m - DiscountRate);

        return Math.Round(discountedCost, 2, MidpointRounding.AwayFromZero);
    }
}
