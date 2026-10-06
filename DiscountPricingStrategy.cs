namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Applies a 10 percent discount to the normal rental cost.
    /// </summary>
    public class DiscountPricingStrategy : IVehiclePricingStrategy
    {
        public decimal CalculateCost(decimal dailyRate, int days)
        {
            decimal normalCost = dailyRate * days;
            return normalCost * 0.90m;
        }
    }
}