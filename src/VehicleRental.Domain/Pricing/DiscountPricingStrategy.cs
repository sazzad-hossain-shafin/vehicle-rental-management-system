namespace VehicleRental.Domain.Pricing;

/// <summary>
/// Promotional pricing: 10% off the standard cost.
/// </summary>
public sealed class DiscountPricingStrategy : PricingStrategyBase
{
    public override string Name => $"Promotional discount ({DiscountPercentText})";

    protected override decimal DiscountRate => 0.10m;
}
