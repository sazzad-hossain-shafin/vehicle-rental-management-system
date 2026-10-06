namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Applies a 20 percent discount for long-term rentals.
    /// </summary>
    public class LongTermPricingStrategy : IVehiclePricingStrategy
    {
        public decimal CalculateCost(decimal dailyRate, int days)
        {
            decimal normalCost = dailyRate * days;
            return normalCost * 0.80m;
        }
    }
}