namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Uses the normal daily rental rate without any discount.
    /// </summary>
    public class NormalPricingStrategy : IVehiclePricingStrategy
    {
        public decimal CalculateCost(decimal dailyRate, int days)
        {
            return dailyRate * days;
        }
    }
}