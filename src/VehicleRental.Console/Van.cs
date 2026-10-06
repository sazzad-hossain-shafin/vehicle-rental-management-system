namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Represents a van that can be rented.
    /// </summary>
    public class Van : Vehicle
    {
        /// <summary>
        /// Creates a new van.
        /// </summary>
        public Van(
            string vehicleId,
            string model,
            decimal dailyRate,
            IVehiclePricingStrategy pricingStrategy)
            : base(vehicleId, model, dailyRate, pricingStrategy)
        {
        }
    }
}