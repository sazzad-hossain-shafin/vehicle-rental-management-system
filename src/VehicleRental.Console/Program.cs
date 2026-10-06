using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.InMemory;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.ConsoleApp;

/// <summary>
/// Temporary console client. It reads input, calls the Application services and
/// prints the results. It holds no data and applies no business rules.
/// </summary>
public class Program
{
    private const string Separator = "----------------------------------------------";

    public static async Task Main(string[] args)
    {
        // Composition root: the only place that chooses the (temporary) in-memory storage.
        var unitOfWork = new InMemoryUnitOfWork();
        var vehicleRepository = new InMemoryVehicleRepository();
        var customerRepository = new InMemoryCustomerRepository();
        var rentalRepository = new InMemoryRentalRepository();

        var vehicles = new VehicleService(vehicleRepository, unitOfWork);
        var rentals = new RentalService(vehicleRepository, customerRepository, rentalRepository, unitOfWork);

        await SampleData.SeedVehiclesAsync(vehicles);

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
                        await ShowAvailableVehiclesAsync(vehicles);
                        break;

                    case "2":
                        await SearchVehiclesAsync(vehicles);
                        break;

                    case "3":
                        await FilterVehiclesAsync(vehicles);
                        break;

                    case "4":
                        await RentVehicleAsync(vehicles, rentals);
                        break;

                    case "5":
                        await ReturnVehicleAsync(rentals);
                        break;

                    case "6":
                        await ShowRentalHistoryAsync(rentals);
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
            catch (Exception ex) when (ex is NotFoundException
                                          or ConflictException
                                          or ArgumentException
                                          or InvalidOperationException)
            {
                Console.WriteLine($"\nError: {ex.Message}");
            }
        }
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

    private static async Task ShowAvailableVehiclesAsync(VehicleService vehicles)
    {
        var available = await vehicles.SearchAsync(
            new VehicleSearchCriteria(Availability: VehicleAvailabilityStatus.Available));

        Console.WriteLine("\nAvailable Vehicles:");

        foreach (VehicleDto vehicle in available)
        {
            Console.WriteLine(DescribeVehicle(vehicle));
        }
    }

    private static async Task SearchVehiclesAsync(VehicleService vehicles)
    {
        Console.Write("\nEnter vehicle type (Car, Motorcycle, Van): ");

        if (!TryParseVehicleType(Console.ReadLine(), out VehicleType type))
        {
            Console.WriteLine("Unknown vehicle type.");
            return;
        }

        ShowSearchResults(await vehicles.SearchAsync(new VehicleSearchCriteria(VehicleType: type)));
    }

    private static async Task FilterVehiclesAsync(VehicleService vehicles)
    {
        Console.Write("\nEnter maximum daily rate: $");

        bool validRate = decimal.TryParse(Console.ReadLine(), out decimal maxRate);

        if (!validRate || maxRate < 0)
        {
            Console.WriteLine("Invalid rate.");
            return;
        }

        ShowSearchResults(await vehicles.SearchAsync(new VehicleSearchCriteria(MaximumDailyRate: maxRate)));
    }

    private static async Task RentVehicleAsync(VehicleService vehicles, RentalService rentals)
    {
        await ShowAvailableVehiclesAsync(vehicles);

        Console.Write("\nEnter vehicle ID: ");
        string vehicleId = Console.ReadLine() ?? "";

        // Checked up front only so the user is not asked for details of an unrentable vehicle;
        // the rental service enforces availability itself.
        VehicleDto vehicle = await vehicles.GetByIdAsync(vehicleId);

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

        bool promotionRequested = AskForPromotion(rentals, days);

        RentalDto rental = await rentals.StartRentalAsync(
            new StartRentalRequest(vehicleId, customerId, customerName, days, promotionRequested));

        Console.WriteLine($"\n{Separator}");
        Console.WriteLine("                RENTAL SUMMARY");
        Console.WriteLine(Separator);
        Console.WriteLine($"Customer: {rental.CustomerName}");
        Console.WriteLine($"Vehicle: {rental.VehicleDisplayName}");
        Console.WriteLine($"Vehicle Type: {rental.VehicleType}");
        Console.WriteLine($"Rental Period: {rental.StartDate:yyyy-MM-dd} to {rental.ExpectedReturnDate:yyyy-MM-dd}");
        Console.WriteLine($"Rental Days: {rental.BillableDays}");
        Console.WriteLine($"Pricing: {rental.PricingDescription}");
        Console.WriteLine($"Total Cost: ${rental.TotalCost:0.00}");
        Console.WriteLine($"Status: {rental.Status}");
        Console.WriteLine(Separator);
        Console.WriteLine("Rental completed successfully.");
    }

    private static async Task ReturnVehicleAsync(RentalService rentals)
    {
        Console.Write("\nEnter vehicle ID to return: ");
        string vehicleId = Console.ReadLine() ?? "";

        RentalDto rental = await rentals.ReturnVehicleAsync(vehicleId);

        Console.WriteLine($"{rental.VehicleDisplayName} has been returned successfully.");
    }

    private static async Task ShowRentalHistoryAsync(RentalService rentals)
    {
        var history = await rentals.GetRentalHistoryAsync();

        Console.WriteLine("\nRental History:");

        if (history.Count == 0)
        {
            Console.WriteLine("No rental records found.");
            return;
        }

        foreach (RentalDto rental in history)
        {
            Console.WriteLine(
                $"{rental.CustomerName} - " +
                $"{rental.VehicleDisplayName} - " +
                $"{rental.BillableDays} days - " +
                $"${rental.TotalCost:0.00} - " +
                $"{rental.PricingDescription} - " +
                $"{rental.Status}");
        }
    }

    /// <summary>
    /// Asks about the promotion only when the application says one can be offered.
    /// </summary>
    private static bool AskForPromotion(RentalService rentals, int days)
    {
        if (!rentals.IsPromotionalDiscountAvailable(days))
        {
            Console.WriteLine("Long-term pricing will be applied to this rental.");
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

    private static void ShowSearchResults(IReadOnlyList<VehicleDto> vehicles)
    {
        if (vehicles.Count == 0)
        {
            Console.WriteLine("\nNo vehicles found.");
            return;
        }

        Console.WriteLine("\nSearch Results:");

        foreach (VehicleDto vehicle in vehicles)
        {
            Console.WriteLine($"{DescribeVehicle(vehicle)} - {vehicle.AvailabilityStatus}");
        }
    }

    private static string DescribeVehicle(VehicleDto vehicle) =>
        $"{vehicle.Id}. {vehicle.DisplayName} ({vehicle.Year}) - {vehicle.VehicleType} - ${vehicle.DailyRate}/day";
}
