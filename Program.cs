using System;
using System.Collections.Generic;

namespace VehicleRentalManagementSystem
{
    /// <summary>
    /// Starts and controls the Vehicle Rental Management System.
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            RentalSystem rentalSystem = new RentalSystem();

            AddSampleVehicles(rentalSystem);

            bool running = true;

            while (running)
            {
                ShowMenu();

                Console.Write("\nSelect an option: ");
                string? choice = Console.ReadLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            rentalSystem.DisplayAvailableVehicles();
                            break;

                        case "2":
                            SearchVehicles(rentalSystem);
                            break;

                        case "3":
                            FilterVehicles(rentalSystem);
                            break;

                        case "4":
                            RentVehicle(rentalSystem);
                            break;

                        case "5":
                            ReturnVehicle(rentalSystem);
                            break;

                        case "6":
                            rentalSystem.DisplayRentalHistory();
                            break;

                        case "7":
                            running = false;
                            Console.WriteLine("\nThank you for using the system.");
                            break;

                        default:
                            Console.WriteLine("\nInvalid option. Please try again.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nError: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Adds sample vehicles using the VehicleFactory.
        /// </summary>
        private static void AddSampleVehicles(RentalSystem rentalSystem)
        {
            IVehiclePricingStrategy normalPricing =
                new NormalPricingStrategy();

            rentalSystem.AddVehicle(
                VehicleFactory.CreateVehicle(
                    "car",
                    "1",
                    "Toyota Corolla",
                    60m,
                    normalPricing));

            rentalSystem.AddVehicle(
                VehicleFactory.CreateVehicle(
                    "motorcycle",
                    "2",
                    "Honda CB500",
                    40m,
                    normalPricing));

            rentalSystem.AddVehicle(
                VehicleFactory.CreateVehicle(
                    "van",
                    "3",
                    "Toyota HiAce",
                    90m,
                    normalPricing));
        }

        /// <summary>
        /// Displays the main menu.
        /// </summary>
        private static void ShowMenu()
        {
            Console.WriteLine("\n==============================================");
            Console.WriteLine("       VEHICLE RENTAL MANAGEMENT SYSTEM");
            Console.WriteLine("==============================================");
            Console.WriteLine("1. View Available Vehicles");
            Console.WriteLine("2. Search Vehicles by Type");
            Console.WriteLine("3. Filter Vehicles by Maximum Daily Rate");
            Console.WriteLine("4. Rent a Vehicle");
            Console.WriteLine("5. Return a Vehicle");
            Console.WriteLine("6. View Rental History");
            Console.WriteLine("7. Exit");
            Console.WriteLine("==============================================");
        }

        /// <summary>
        /// Searches vehicles by their type.
        /// </summary>
        private static void SearchVehicles(RentalSystem rentalSystem)
        {
            Console.Write("\nEnter vehicle type (Car, Motorcycle, Van): ");
            string type = Console.ReadLine() ?? "";

            List<Vehicle> results =
                rentalSystem.SearchByType(type);

            DisplayVehicles(results);
        }

        /// <summary>
        /// Filters vehicles using a maximum daily rate.
        /// </summary>
        private static void FilterVehicles(RentalSystem rentalSystem)
        {
            Console.Write("\nEnter maximum daily rate: $");

            bool validRate =
                decimal.TryParse(Console.ReadLine(), out decimal maxRate);

            if (!validRate || maxRate < 0)
            {
                Console.WriteLine("Invalid rate.");
                return;
            }

            List<Vehicle> results =
                rentalSystem.FilterByMaximumRate(maxRate);

            DisplayVehicles(results);
        }

        /// <summary>
        /// Handles the vehicle rental process.
        /// </summary>
        private static void RentVehicle(RentalSystem rentalSystem)
        {
            rentalSystem.DisplayAvailableVehicles();

            Console.Write("\nEnter vehicle ID: ");
            string vehicleId = Console.ReadLine() ?? "";

            Vehicle? vehicle =
                FindVehicleById(rentalSystem, vehicleId);

            if (vehicle == null)
            {
                Console.WriteLine("Vehicle not found.");
                return;
            }

            if (vehicle.Status != RentalStatus.Available)
            {
                Console.WriteLine("Vehicle is not available.");
                return;
            }

            Console.Write("Enter rental days: ");

            bool validDays =
                int.TryParse(Console.ReadLine(), out int days);

            if (!validDays || days <= 0)
            {
                Console.WriteLine("Invalid rental days.");
                return;
            }

            Console.Write("Enter customer ID: ");
            string customerId = Console.ReadLine() ?? "";

            Console.Write("Enter customer name: ");
            string customerName = Console.ReadLine() ?? "";

            if (string.IsNullOrWhiteSpace(customerName))
            {
                Console.WriteLine("Customer name cannot be empty.");
                return;
            }

            IVehiclePricingStrategy pricingStrategy =
                SelectPricingStrategy(days);

            vehicle.SetPricingStrategy(pricingStrategy);

            Customer customer =
                new Customer(customerId, customerName);

            Rental rental =
                rentalSystem.RentVehicle(
                    customer,
                    vehicle,
                    days);

            Console.WriteLine("\n----------------------------------------------");
            Console.WriteLine("                RENTAL SUMMARY");
            Console.WriteLine("----------------------------------------------");
            Console.WriteLine($"Customer: {rental.Customer.Name}");
            Console.WriteLine($"Vehicle: {rental.Vehicle.Model}");
            Console.WriteLine(
                $"Vehicle Type: {rental.Vehicle.GetType().Name}");
            Console.WriteLine($"Rental Days: {rental.RentalDays}");
            Console.WriteLine(
                $"Pricing: {pricingStrategy.GetType().Name}");
            Console.WriteLine(
                $"Total Cost: ${rental.GetTotalCost():0.00}");
            Console.WriteLine($"Status: {rental.Vehicle.Status}");
            Console.WriteLine("----------------------------------------------");
            Console.WriteLine("Rental completed successfully.");
        }

        /// <summary>
        /// Returns a rented vehicle.
        /// </summary>
        private static void ReturnVehicle(RentalSystem rentalSystem)
        {
            Console.Write("\nEnter vehicle ID to return: ");
            string vehicleId = Console.ReadLine() ?? "";

            Vehicle? vehicle =
                FindVehicleById(rentalSystem, vehicleId);

            if (vehicle == null)
            {
                Console.WriteLine("Vehicle not found.");
                return;
            }

            if (vehicle.Status != RentalStatus.Rented)
            {
                Console.WriteLine("This vehicle is not currently rented.");
                return;
            }

            rentalSystem.ReturnVehicle(vehicle);

            Console.WriteLine(
                $"{vehicle.Model} has been returned successfully.");
        }

        /// <summary>
        /// Chooses a pricing strategy for the rental.
        /// </summary>
        private static IVehiclePricingStrategy SelectPricingStrategy(
            int days)
        {
            if (days >= 7)
            {
                Console.WriteLine(
                    "Long-term pricing applied: 20% discount.");

                return new LongTermPricingStrategy();
            }

            Console.Write(
                "Apply promotional 10% discount? (Y/N): ");

            string answer =
                (Console.ReadLine() ?? "").Trim().ToUpper();

            if (answer == "Y")
            {
                return new DiscountPricingStrategy();
            }

            return new NormalPricingStrategy();
        }

        /// <summary>
        /// Finds a vehicle using its ID.
        /// </summary>
        private static Vehicle? FindVehicleById(
            RentalSystem rentalSystem,
            string vehicleId)
        {
            foreach (Vehicle vehicle in rentalSystem.Vehicles)
            {
                if (vehicle.VehicleId == vehicleId)
                {
                    return vehicle;
                }
            }

            return null;
        }

        /// <summary>
        /// Displays a list of vehicles.
        /// </summary>
        private static void DisplayVehicles(List<Vehicle> vehicles)
        {
            if (vehicles.Count == 0)
            {
                Console.WriteLine("\nNo vehicles found.");
                return;
            }

            Console.WriteLine("\nSearch Results:");

            foreach (Vehicle vehicle in vehicles)
            {
                Console.WriteLine(
                    $"{vehicle.VehicleId}. " +
                    $"{vehicle.Model} - " +
                    $"{vehicle.GetType().Name} - " +
                    $"${vehicle.DailyRate}/day - " +
                    $"{vehicle.Status}");
            }
        }
    }
}