namespace VehicleRental.Domain.Pricing;

/// <summary>
/// Decides which pricing strategy applies to a rental. The strategies themselves
/// only calculate; this class holds the rules for choosing between them.
/// </summary>
public static class PricingPolicy
{
    /// <summary>Rentals of at least this many billable days receive long-term pricing.</summary>
    public const int LongTermThresholdDays = 7;

    private static readonly IVehiclePricingStrategy Normal = new NormalPricingStrategy();
    private static readonly IVehiclePricingStrategy Promotional = new DiscountPricingStrategy();
    private static readonly IVehiclePricingStrategy LongTerm = new LongTermPricingStrategy();

    /// <summary>
    /// Whether a promotional discount can be offered. Long-term rentals already
    /// get a better discount, so the promotion is not available for them.
    /// </summary>
    public static bool IsPromotionalDiscountAvailable(int billableDays) =>
        billableDays < LongTermThresholdDays;

    /// <summary>
    /// Selects the strategy for a rental. Long-term pricing always wins for long
    /// rentals; a promotional request never overrides it and discounts never stack.
    /// </summary>
    public static IVehiclePricingStrategy SelectStrategy(int billableDays, bool promotionalDiscountRequested)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(billableDays, 1);

        if (!IsPromotionalDiscountAvailable(billableDays))
        {
            return LongTerm;
        }

        return promotionalDiscountRequested ? Promotional : Normal;
    }
}
