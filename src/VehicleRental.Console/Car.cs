namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Represents a car that can be rented.
    /// </summary>
    public class Car : Vehicle
    {
        /// <summary>
        /// Creates a new car.
        /// </summary>
        public Car(
            string vehicleId,
            string model,
            decimal dailyRate,
            IVehiclePricingStrategy pricingStrategy)
            : base(vehicleId, model, dailyRate, pricingStrategy)
        {
        }
    }
}