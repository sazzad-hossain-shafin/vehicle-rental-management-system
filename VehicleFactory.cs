namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Creates different types of vehicles.
    /// </summary>
    public static class VehicleFactory
    {
        /// <summary>
        /// Creates a vehicle based on the selected vehicle type.
        /// </summary>
        public static Vehicle CreateVehicle(
            string type,
            string vehicleId,
            string model,
            decimal dailyRate,
            IVehiclePricingStrategy pricingStrategy)
        {
            switch (type.ToLower())
            {
                case "car":
                    return new Car(
                        vehicleId,
                        model,
                        dailyRate,
                        pricingStrategy);

                case "motorcycle":
                    return new Motorcycle(
                        vehicleId,
                        model,
                        dailyRate,
                        pricingStrategy);

                case "van":
                    return new Van(
                        vehicleId,
                        model,
                        dailyRate,
                        pricingStrategy);

                default:
                    throw new ArgumentException("Invalid vehicle type.");
            }
        }
    }
}