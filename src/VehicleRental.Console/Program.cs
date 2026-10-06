using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.ConsoleApp;

/// <summary>
/// Temporary console client. It reads input and prints results; vehicle,
/// customer, rental and pricing rules all come from the Domain project.
/// </summary>
public class Program
{
    private const string Separator = "----------------------------------------------";

    public static void Main(string[] args)
    {
        var store = new InMemoryRentalStore();
        AddSampleVehicles(store);

        bool running = true;

        while (running)
        {
            ShowMenu();

            Console.Write("\nSelect an option: ");
            string? choice = Console.ReadLine();

            if (choice is null)
            {
                // Input has ended (for example, redirected input), so there is nothing more to read.
                break;
            }

            try
            {
                switch (choice)
                {
                    case "1":
                        ShowAvailableVehicles(store);
                        break;

                    case "2":
                        SearchVehicles(store);
                        break;

                    case "3":
                        FilterVehicles(store);
                        break;

                    case "4":
                        RentVehicle(store);
                        break;

                    case "5":
                        ReturnVehicle(store);
                        break;

                    case "6":
                        ShowRentalHistory(store);
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
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Console.WriteLine($"\nError: {ex.Message}");
            }
        }
    }

    private static void AddSampleVehicles(InMemoryRentalStore store)
    {
        store.AddVehicle(new Vehicle("1", "Toyota", "Corolla", 2022, VehicleType.Car, 60m));
        store.AddVehicle(new Vehicle("2", "Honda", "CB500", 2021, VehicleType.Motorcycle, 40m));
        store.AddVehicle(new Vehicle("3", "Toyota", "HiAce", 2021, VehicleType.Van, 90m));
    }

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

    private static void ShowAvailableVehicles(InMemoryRentalStore store)
    {
        Console.WriteLine("\nAvailable Vehicles:");

        foreach (Vehicle vehicle in store.Vehicles
                     .Where(v => v.AvailabilityStatus == VehicleAvailabilityStatus.Available))
        {
            Console.WriteLine(DescribeVehicle(vehicle));
        }
    }

    private static void SearchVehicles(InMemoryRentalStore store)
    {
        Console.Write("\nEnter vehicle type (Car, Motorcycle, Van): ");

        if (!TryParseVehicleType(Console.ReadLine(), out VehicleType type))
        {
            Console.WriteLine("Unknown vehicle type.");
            return;
        }

        ShowSearchResults(store.FindByType(type));
    }

    private static void FilterVehicles(InMemoryRentalStore store)
    {
        Console.Write("\nEnter maximum daily rate: $");

        bool validRate = decimal.TryParse(Console.ReadLine(), out decimal maxRate);

        if (!validRate || maxRate < 0)
        {
            Console.WriteLine("Invalid rate.");
            return;
        }

        ShowSearchResults(store.FindByMaximumRate(maxRate));
    }

    private static void RentVehicle(InMemoryRentalStore store)
    {
        ShowAvailableVehicles(store);

        Console.Write("\nEnter vehicle ID: ");
        Vehicle? vehicle = store.FindVehicle(Console.ReadLine() ?? "");

        if (vehicle is null)
        {
            Console.WriteLine("Vehicle not found.");
            return;
        }

        if (vehicle.AvailabilityStatus != VehicleAvailabilityStatus.Available)
        {
            Console.WriteLine("Vehicle is not available.");
            return;
        }

        Console.Write("Enter rental days: ");

        bool validDays = int.TryParse(Console.ReadLine(), out int days);

        if (!validDays || days <= 0)
        {
            Console.WriteLine("Invalid rental days.");
            return;
        }

        Console.Write("Enter customer ID: ");
        string customerId = Console.ReadLine() ?? "";

        Console.Write("Enter customer name: ");
        string customerName = Console.ReadLine() ?? "";

        var customer = new Customer(customerId, customerName);

        DateOnly startDate = DateOnly.FromDateTime(DateTime.Today);
        DateOnly returnDate = startDate.AddDays(days);

        bool promotionRequested = AskForPromotion(days);
        IVehiclePricingStrategy strategy = PricingPolicy.SelectStrategy(days, promotionRequested);

        Rental rental = Rental.Start(customer, vehicle, startDate, returnDate, strategy);
        store.AddRental(rental);

        Console.WriteLine($"\n{Separator}");
        Console.WriteLine("                RENTAL SUMMARY");
        Console.WriteLine(Separator);
        Console.WriteLine($"Customer: {rental.Customer.Name}");
        Console.WriteLine($"Vehicle: {rental.Vehicle.DisplayName}");
        Console.WriteLine($"Vehicle Type: {rental.Vehicle.VehicleType}");
        Console.WriteLine($"Rental Period: {rental.StartDate:yyyy-MM-dd} to {rental.ExpectedReturnDate:yyyy-MM-dd}");
        Console.WriteLine($"Rental Days: {rental.BillableDays}");
        Console.WriteLine($"Pricing: {rental.PricingDescription}");
        Console.WriteLine($"Total Cost: ${rental.TotalCost:0.00}");
        Console.WriteLine($"Status: {rental.Vehicle.AvailabilityStatus}");
        Console.WriteLine(Separator);
        Console.WriteLine("Rental completed successfully.");
    }

    private static void ReturnVehicle(InMemoryRentalStore store)
    {
        Console.Write("\nEnter vehicle ID to return: ");
        Vehicle? vehicle = store.FindVehicle(Console.ReadLine() ?? "");

        if (vehicle is null)
        {
            Console.WriteLine("Vehicle not found.");
            return;
        }

        Rental? rental = store.FindActiveRental(vehicle);

        if (rental is null)
        {
            Console.WriteLine("This vehicle is not currently rented.");
            return;
        }

        rental.Complete(DateOnly.FromDateTime(DateTime.Today));

        Console.WriteLine($"{vehicle.DisplayName} has been returned successfully.");
    }

    private static void ShowRentalHistory(InMemoryRentalStore store)
    {
        Console.WriteLine("\nRental History:");

        if (store.Rentals.Count == 0)
        {
            Console.WriteLine("No rental records found.");
            return;
        }

        foreach (Rental rental in store.Rentals)
        {
            Console.WriteLine(
                $"{rental.Customer.Name} - " +
                $"{rental.Vehicle.DisplayName} - " +
                $"{rental.BillableDays} days - " +
                $"${rental.TotalCost:0.00} - " +
                $"{rental.PricingDescription} - " +
                $"{rental.Status}");
        }
    }

    /// <summary>
    /// Asks about the promotion only when the domain says one can be offered.
    /// </summary>
    private static bool AskForPromotion(int days)
    {
        if (!PricingPolicy.IsPromotionalDiscountAvailable(days))
        {
            Console.WriteLine(
                $"Long-term pricing applies to rentals of {PricingPolicy.LongTermThresholdDays} days or more.");
            return false;
        }

        Console.Write("Apply promotional discount? (Y/N): ");

        string answer = (Console.ReadLine() ?? "").Trim();

        return answer.Equals("Y", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a vehicle type by name, so numbers such as "1" are not accepted as enum values.
    /// </summary>
    private static bool TryParseVehicleType(string? input, out VehicleType type)
    {
        type = default;

        string name = (input ?? "").Trim();

        return Enum.GetNames<VehicleType>().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))
               && Enum.TryParse(name, ignoreCase: true, out type);
    }

    private static void ShowSearchResults(IReadOnlyList<Vehicle> vehicles)
    {
        if (vehicles.Count == 0)
        {
            Console.WriteLine("\nNo vehicles found.");
            return;
        }

        Console.WriteLine("\nSearch Results:");

        foreach (Vehicle vehicle in vehicles)
        {
            Console.WriteLine($"{DescribeVehicle(vehicle)} - {vehicle.AvailabilityStatus}");
        }
    }

    private static string DescribeVehicle(Vehicle vehicle) =>
        $"{vehicle.Id}. {vehicle.DisplayName} ({vehicle.Year}) - {vehicle.VehicleType} - ${vehicle.DailyRate}/day";
}
