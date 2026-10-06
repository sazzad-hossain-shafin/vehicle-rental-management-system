namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Defines a pricing strategy for calculating vehicle rental cost.
    /// </summary>
    public interface IVehiclePricingStrategy
    {
        decimal CalculateCost(decimal dailyRate, int days);
    }
}