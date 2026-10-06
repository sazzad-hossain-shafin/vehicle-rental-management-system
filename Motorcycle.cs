namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Represents a motorcycle that can be rented.
    /// </summary>
    public class Motorcycle : Vehicle
    {
        /// <summary>
        /// Creates a new motorcycle.
        /// </summary>
        public Motorcycle(
            string vehicleId,
            string model,
            decimal dailyRate,
            IVehiclePricingStrategy pricingStrategy)
            : base(vehicleId, model, dailyRate, pricingStrategy)
        {
        }
    }
}