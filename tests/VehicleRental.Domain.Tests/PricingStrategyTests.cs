using VehicleRental.Domain.Pricing;

namespace VehicleRental.Domain.Tests;

public class PricingStrategyTests
{
    [Theory]
    [InlineData(60, 1, 60)]
    [InlineData(60, 3, 180)]
    [InlineData(90, 6, 540)]
    public void NormalPricing_CalculatesRateTimesDays(double rate, int days, double expected)
    {
        var cost = new NormalPricingStrategy().CalculateCost((decimal)rate, days);

        Assert.Equal((decimal)expected, cost);
    }

    [Theory]
    [InlineData(100, 1, 90)]
    [InlineData(100, 5, 450)]
    [InlineData(60, 3, 162)]
    public void DiscountPricing_TakesTenPercentOff(double rate, int days, double expected)
    {
        var cost = new DiscountPricingStrategy().CalculateCost((decimal)rate, days);

        Assert.Equal((decimal)expected, cost);
    }

    [Theory]
    [InlineData(100, 7, 560)]
    [InlineData(100, 10, 800)]
    [InlineData(40, 7, 224)]
    public void LongTermPricing_TakesTwentyPercentOff(double rate, int days, double expected)
    {
        var cost = new LongTermPricingStrategy().CalculateCost((decimal)rate, days);

        Assert.Equal((decimal)expected, cost);
    }

    [Fact]
    public void DiscountPricing_RoundsToTwoDecimalPlaces()
    {
        // 59.99 x 3 x 0.90 = 161.973
        var cost = new DiscountPricingStrategy().CalculateCost(59.99m, 3);

        Assert.Equal(161.97m, cost);
    }

    [Fact]
    public void Pricing_RoundsHalfCentsAwayFromZero()
    {
        // 0.25 x 1 x 0.90 = 0.225, which rounds up to 0.23 (banker's rounding would give 0.22)
        var cost = new DiscountPricingStrategy().CalculateCost(0.25m, 1);

        Assert.Equal(0.23m, cost);
    }

    [Fact]
    public void LongTermPricing_IsCheaperThanDiscountPricing_ForTheSameRental()
    {
        var discount = new DiscountPricingStrategy().CalculateCost(100m, 7);
        var longTerm = new LongTermPricingStrategy().CalculateCost(100m, 7);

        Assert.True(longTerm < discount);
    }

    public static TheoryData<IVehiclePricingStrategy> AllStrategies => new()
    {
        new NormalPricingStrategy(),
        new DiscountPricingStrategy(),
        new LongTermPricingStrategy()
    };

    [Theory]
    [MemberData(nameof(AllStrategies))]
    public void CalculateCost_WithZeroDays_ThrowsArgumentOutOfRangeException(IVehiclePricingStrategy strategy)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => strategy.CalculateCost(100m, 0));
    }

    [Theory]
    [MemberData(nameof(AllStrategies))]
    public void CalculateCost_WithNonPositiveRate_ThrowsArgumentOutOfRangeException(IVehiclePricingStrategy strategy)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => strategy.CalculateCost(0m, 3));
    }

    [Fact]
    public void Names_DescribeEachStrategy()
    {
        Assert.Equal("Normal pricing", new NormalPricingStrategy().Name);
        Assert.Contains("10%", new DiscountPricingStrategy().Name);
        Assert.Contains("20%", new LongTermPricingStrategy().Name);
    }
}
