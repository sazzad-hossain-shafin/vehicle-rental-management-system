namespace VehicleRental.Domain.Pricing;

/// <summary>
/// Standard pricing: the daily rate multiplied by the number of days.
/// </summary>
public sealed class NormalPricingStrategy : PricingStrategyBase
{
    public override string Name => "Normal pricing";

    protected override decimal DiscountRate => 0m;
}
