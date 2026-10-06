using System;
using System.Collections.Generic;
using System.Linq;

namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Manages vehicles and rental records in the system.
    /// </summary>
    public class RentalSystem
    {
        public List<Vehicle> Vehicles { get; }
        public List<Rental> Rentals { get; }

        /// <summary>
        /// Creates a new rental system.
        /// </summary>
        public RentalSystem()
        {
            Vehicles = new List<Vehicle>();
            Rentals = new List<Rental>();
        }

        /// <summary>
        /// Adds a vehicle to the system.
        /// </summary>
        public void AddVehicle(Vehicle vehicle)
        {
            Vehicles.Add(vehicle);
        }

        /// <summary>
        /// Displays all vehicles that are available for rent.
        /// </summary>
        public void DisplayAvailableVehicles()
        {
            Console.WriteLine("\nAvailable Vehicles:");

            foreach (Vehicle vehicle in Vehicles)
            {
                if (vehicle.Status == RentalStatus.Available)
                {
                    Console.WriteLine(
                        $"{vehicle.VehicleId}. {vehicle.Model} - {vehicle.GetType().Name} - ${vehicle.DailyRate}/day");
                }
            }
        }

        /// <summary>
        /// Searches vehicles by type.
        /// </summary>
        public List<Vehicle> SearchByType(string type)
        {
            return Vehicles
                .Where(v => v.GetType().Name.Equals(
                    type,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Filters vehicles by maximum daily rental rate.
        /// </summary>
        public List<Vehicle> FilterByMaximumRate(decimal maxRate)
        {
            return Vehicles
                .Where(v => v.DailyRate <= maxRate)
                .ToList();
        }

        /// <summary>
        /// Creates and stores a new rental.
        /// </summary>
        public Rental RentVehicle(
            Customer customer,
            Vehicle vehicle,
            int days)
        {
            if (vehicle.Status != RentalStatus.Available)
            {
                throw new InvalidOperationException(
                    "Vehicle is not available.");
            }

            if (days <= 0)
            {
                throw new ArgumentException(
                    "Rental days must be greater than zero.");
            }

            Rental rental = new Rental(
                customer,
                vehicle,
                days);

            vehicle.Rent();
            Rentals.Add(rental);

            return rental;
        }

        /// <summary>
        /// Returns a rented vehicle.
        /// </summary>
        public void ReturnVehicle(Vehicle vehicle)
        {
            vehicle.ReturnVehicle();
        }

        /// <summary>
        /// Displays previous rental records.
        /// </summary>
        public void DisplayRentalHistory()
        {
            Console.WriteLine("\nRental History:");

            if (Rentals.Count == 0)
            {
                Console.WriteLine("No rental records found.");
                return;
            }

            foreach (Rental rental in Rentals)
            {
                Console.WriteLine(
                    $"{rental.Customer.Name} - " +
                    $"{rental.Vehicle.Model} - " +
                    $"{rental.RentalDays} days - " +
                    $"${rental.GetTotalCost():0.00}");
            }
        }
    }
}