using VehicleRental.Domain.Pricing;

namespace VehicleRental.Domain.Tests;

public class PricingPolicyTests
{
    [Fact]
    public void SelectStrategy_SixDaysWithoutPromotion_ReturnsNormalPricing()
    {
        var strategy = PricingPolicy.SelectStrategy(6, promotionalDiscountRequested: false);

        Assert.IsType<NormalPricingStrategy>(strategy);
    }

    [Fact]
    public void SelectStrategy_SixDaysWithPromotion_ReturnsDiscountPricing()
    {
        var strategy = PricingPolicy.SelectStrategy(6, promotionalDiscountRequested: true);

        Assert.IsType<DiscountPricingStrategy>(strategy);
    }

    [Fact]
    public void SelectStrategy_SevenDaysWithoutPromotion_ReturnsLongTermPricing()
    {
        var strategy = PricingPolicy.SelectStrategy(7, promotionalDiscountRequested: false);

        Assert.IsType<LongTermPricingStrategy>(strategy);
    }

    [Fact]
    public void SelectStrategy_SevenDaysWithPromotion_StillReturnsLongTermPricing()
    {
        var strategy = PricingPolicy.SelectStrategy(7, promotionalDiscountRequested: true);

        Assert.IsType<LongTermPricingStrategy>(strategy);
    }

    [Theory]
    [InlineData(1, false, typeof(NormalPricingStrategy))]
    [InlineData(1, true, typeof(DiscountPricingStrategy))]
    [InlineData(30, true, typeof(LongTermPricingStrategy))]
    public void SelectStrategy_AtDurationExtremes_ReturnsExpectedStrategy(
        int days, bool promotion, Type expected)
    {
        var strategy = PricingPolicy.SelectStrategy(days, promotion);

        Assert.IsType(expected, strategy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void SelectStrategy_WithInvalidDuration_ThrowsArgumentOutOfRangeException(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PricingPolicy.SelectStrategy(days, promotionalDiscountRequested: false));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(14, false)]
    public void IsPromotionalDiscountAvailable_DependsOnLongTermThreshold(int days, bool expected)
    {
        Assert.Equal(expected, PricingPolicy.IsPromotionalDiscountAvailable(days));
    }
}
