namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Represents a customer who can rent a vehicle.
    /// </summary>
    public class Customer
    {
        public string CustomerId { get; }
        public string Name { get; }

        /// <summary>
        /// Creates a new customer.
        /// </summary>
        public Customer(string customerId, string name)
        {
            CustomerId = customerId;
            Name = name;
        }
    }
}