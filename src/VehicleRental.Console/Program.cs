using Microsoft.Extensions.Configuration;
using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure;

namespace VehicleRental.ConsoleApp;

/// <summary>
/// Temporary console client. It reads input, calls the Application services and prints the
/// results. It holds no data and applies no business rules; storage is PostgreSQL through the
/// Infrastructure project.
/// </summary>
public class Program
{
    private const string Separator = "----------------------------------------------";

    private sealed record Services(VehicleService Vehicles, RentalService Rentals);

    public static async Task<int> Main(string[] args)
    {
        string? connectionString = LoadConnectionString();

        if (connectionString is null)
        {
            PrintMissingConfiguration();
            return 1;
        }

        try
        {
            if (!await IsDatabaseReadyAsync(connectionString))
            {
                return 1;
            }

            await SeedSampleDataAsync(connectionString);
            await RunMenuAsync(connectionString);

            return 0;
        }
        catch (Exception ex)
        {
            // Anything not handled per command (a lost database connection, for example).
            Console.WriteLine($"\nUnexpected error: {ex.Message}");
            return 2;
        }
    }

    private static async Task RunMenuAsync(string connectionString)
    {
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

            if (choice == "7")
            {
                Console.WriteLine("\nThank you for using the system.");
                break;
            }

            // A new database session per command, so nothing stale is kept between commands.
            await using PersistenceSession session = PersistenceSession.Create(connectionString);
            Services services = CreateServices(session);

            try
            {
                switch (choice)
                {
                    case "1":
                        await ShowAvailableVehiclesAsync(services.Vehicles);
                        break;

                    case "2":
                        await SearchVehiclesAsync(services.Vehicles);
                        break;

                    case "3":
                        await FilterVehiclesAsync(services.Vehicles);
                        break;

                    case "4":
                        await RentVehicleAsync(services);
                        break;

                    case "5":
                        await ReturnVehicleAsync(services.Rentals);
                        break;

                    case "6":
                        await ShowRentalHistoryAsync(services.Rentals);
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

    private static Services CreateServices(PersistenceSession session) =>
        new(
            new VehicleService(session.Vehicles, session.UnitOfWork),
            new RentalService(session.Vehicles, session.Customers, session.Rentals, session.UnitOfWork));

    // ----- Configuration and startup -----

    /// <summary>
    /// Reads the connection string from user secrets, an optional git-ignored local file or the
    /// environment (later sources win). Nothing secret is stored in the repository.
    /// </summary>
    private static string? LoadConnectionString()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        string? connectionString = configuration.GetConnectionString(PersistenceSession.ConnectionStringName);

        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }

    private static void PrintMissingConfiguration()
    {
        Console.WriteLine("No database connection string is configured.");
        Console.WriteLine();
        Console.WriteLine("Set 'ConnectionStrings:VehicleRentalDatabase' with user secrets (recommended):");
        Console.WriteLine("  dotnet user-secrets set \"ConnectionStrings:VehicleRentalDatabase\" \"<connection string>\" \\");
        Console.WriteLine("      --project src/VehicleRental.Console");
        Console.WriteLine();
        Console.WriteLine($"or with the {PersistenceSession.ConnectionStringEnvironmentVariable} environment variable.");
        Console.WriteLine("See the README for the connection string format and database setup.");
    }

    private static async Task<bool> IsDatabaseReadyAsync(string connectionString)
    {
        await using PersistenceSession session = PersistenceSession.Create(connectionString);

        switch (await session.CheckDatabaseAsync())
        {
            case DatabaseStatus.Ready:
                return true;

            case DatabaseStatus.MigrationsPending:
                Console.WriteLine("The database schema is missing or out of date. Apply the migrations first:");
                Console.WriteLine("  dotnet ef database update --project src/VehicleRental.Infrastructure");
                Console.WriteLine($"(with the {PersistenceSession.ConnectionStringEnvironmentVariable} environment variable set)");
                return false;

            default:
                Console.WriteLine("Could not connect to the database. Check that PostgreSQL is running and that");
                Console.WriteLine("the connection string, database name and credentials are correct.");
                return false;
        }
    }

    private static async Task SeedSampleDataAsync(string connectionString)
    {
        await using PersistenceSession session = PersistenceSession.Create(connectionString);

        if (await SampleData.SeedVehiclesAsync(CreateServices(session).Vehicles))
        {
            Console.WriteLine("Added the sample vehicles to the empty fleet.");
        }
    }

    // ----- Menu actions -----

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

    private static async Task RentVehicleAsync(Services services)
    {
        await ShowAvailableVehiclesAsync(services.Vehicles);

        Console.Write("\nEnter vehicle registration number: ");
        string registrationNumber = Console.ReadLine() ?? "";

        // Checked up front only so the user is not asked for details of an unrentable vehicle;
        // the rental service enforces availability itself.
        VehicleDto vehicle = await services.Vehicles.GetByRegistrationNumberAsync(registrationNumber);

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

        Console.Write("Enter customer number: ");
        string customerNumber = Console.ReadLine() ?? "";

        Console.Write("Enter customer name: ");
        string customerName = Console.ReadLine() ?? "";

        bool promotionRequested = AskForPromotion(services.Rentals, days);

        RentalDto rental = await services.Rentals.StartRentalAsync(
            new StartRentalRequest(registrationNumber, customerNumber, customerName, days, promotionRequested));

        Console.WriteLine($"\n{Separator}");
        Console.WriteLine("                RENTAL SUMMARY");
        Console.WriteLine(Separator);
        Console.WriteLine($"Customer: {rental.CustomerName} ({rental.CustomerNumber})");
        Console.WriteLine($"Vehicle: {rental.VehicleDisplayName} ({rental.VehicleRegistrationNumber})");
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
        Console.Write("\nEnter vehicle registration number to return: ");
        string registrationNumber = Console.ReadLine() ?? "";

        RentalDto rental = await rentals.ReturnVehicleAsync(registrationNumber);

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
                $"{rental.VehicleDisplayName} ({rental.VehicleRegistrationNumber}) - " +
                $"{rental.StartDate:yyyy-MM-dd} - " +
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
        $"{vehicle.RegistrationNumber}. {vehicle.DisplayName} ({vehicle.Year}) - {vehicle.VehicleType} - ${vehicle.DailyRate}/day";
}
