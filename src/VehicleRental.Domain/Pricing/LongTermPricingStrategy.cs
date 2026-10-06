namespace VehicleRental.Domain.Pricing;

/// <summary>
/// Long-term pricing: 20% off the standard cost.
/// </summary>
public sealed class LongTermPricingStrategy : PricingStrategyBase
{
    public override string Name => $"Long-term discount ({DiscountRate:P0})";

    protected override decimal DiscountRate => 0.20m;
}
