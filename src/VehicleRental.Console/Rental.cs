namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Represents a rental record for a customer and vehicle.
    /// </summary>
    public class Rental
    {
        public Customer Customer { get; }
        public Vehicle Vehicle { get; }
        public int RentalDays { get; }

        /// <summary>
        /// Creates a new rental record.
        /// </summary>
        public Rental(Customer customer, Vehicle vehicle, int rentalDays)
        {
            Customer = customer;
            Vehicle = vehicle;
            RentalDays = rentalDays;
        }

        /// <summary>
        /// Gets the total rental cost.
        /// </summary>
        public decimal GetTotalCost()
        {
            return Vehicle.CalculateRentalCost(RentalDays);
        }
    }
}