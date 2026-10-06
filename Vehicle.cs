namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Represents a general vehicle that can be rented.
    /// </summary>
    public abstract class Vehicle
    {
        public string VehicleId { get; }
        public string Model { get; }
        public decimal DailyRate { get; }
        public RentalStatus Status { get; private set; }

        public IVehiclePricingStrategy PricingStrategy { get; private set; }

        /// <summary>
        /// Creates a new vehicle with its basic details.
        /// </summary>
        public Vehicle(
            string vehicleId,
            string model,
            decimal dailyRate,
            IVehiclePricingStrategy pricingStrategy)
        {
            VehicleId = vehicleId;
            Model = model;
            DailyRate = dailyRate;
            PricingStrategy = pricingStrategy;
            Status = RentalStatus.Available;
        }

        /// <summary>
        /// Changes the pricing strategy used by this vehicle.
        /// </summary>
        public void SetPricingStrategy(IVehiclePricingStrategy pricingStrategy)
        {
            PricingStrategy = pricingStrategy;
        }

        /// <summary>
        /// Calculates the rental cost using the selected pricing strategy.
        /// </summary>
        public virtual decimal CalculateRentalCost(int days)
        {
            return PricingStrategy.CalculateCost(DailyRate, days);
        }

        /// <summary>
        /// Changes the vehicle status to rented.
        /// </summary>
        public void Rent()
        {
            Status = RentalStatus.Rented;
        }

        /// <summary>
        /// Returns the vehicle and makes it available again.
        /// </summary>
        public void ReturnVehicle()
        {
            Status = RentalStatus.Available;
        }
    }
}