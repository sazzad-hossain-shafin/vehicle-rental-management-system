using System.Text.RegularExpressions;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Entities;

/// <summary>
/// A vehicle in the rental fleet. It has a generated, permanent <see cref="Id"/> and a
/// business identifier, its <see cref="RegistrationNumber"/>. Its descriptive details are
/// fixed at creation; only its availability changes, and only through
/// <see cref="MarkAsRented"/> and <see cref="MarkAsAvailable"/>.
/// </summary>
public partial class Vehicle
{
    public const int MinimumYear = 1900;
    public const int RegistrationNumberMaxLength = 12;
    public const int MakeMaxLength = 50;
    public const int ModelMaxLength = 50;

    // 2-12 characters: letters, digits, spaces and hyphens, starting and ending with a letter or digit.
    [GeneratedRegex(@"^[A-Z0-9](?:[A-Z0-9 \-]{0,10}[A-Z0-9])$")]
    private static partial Regex RegistrationNumberPattern();

    /// <summary>The permanent internal identifier. It never changes, unlike business identifiers.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The vehicle's registration (plate) number, stored trimmed and upper-case. It is the
    /// identifier people use and is unique across the fleet.
    /// </summary>
    public string RegistrationNumber { get; private set; }

    public string Make { get; private set; }
    public string Model { get; private set; }
    public int Year { get; private set; }
    public VehicleType VehicleType { get; private set; }
    public decimal DailyRate { get; private set; }
    public VehicleAvailabilityStatus AvailabilityStatus { get; private set; }

    /// <summary>Make and model, for example "Toyota Corolla".</summary>
    public string DisplayName => $"{Make} {Model}";

    /// <summary>
    /// For the persistence layer only. It bypasses validation because it is used to rebuild
    /// vehicles that were already validated when first created.
    /// </summary>
    private Vehicle()
    {
        RegistrationNumber = null!;
        Make = null!;
        Model = null!;
    }

    /// <summary>
    /// Creates an available vehicle with a new identifier.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The registration number is not valid, or the make or model is empty or too long.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The year is outside the supported range, the vehicle type is undefined,
    /// or the daily rate is not greater than zero.
    /// </exception>
    public Vehicle(
        string registrationNumber,
        string make,
        string model,
        int year,
        VehicleType vehicleType,
        decimal dailyRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, MinimumYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, DateTime.UtcNow.Year + 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dailyRate, 0m);

        string normalizedRegistration = NormalizeRegistrationNumber(registrationNumber);

        if (!RegistrationNumberPattern().IsMatch(normalizedRegistration))
        {
            throw new ArgumentException(
                $"The registration number must be 2-{RegistrationNumberMaxLength} characters: " +
                "letters, digits, spaces or hyphens, starting and ending with a letter or digit.",
                nameof(registrationNumber));
        }

        if (make.Trim().Length > MakeMaxLength)
        {
            throw new ArgumentException($"The make cannot exceed {MakeMaxLength} characters.", nameof(make));
        }

        if (model.Trim().Length > ModelMaxLength)
        {
            throw new ArgumentException($"The model cannot exceed {ModelMaxLength} characters.", nameof(model));
        }

        if (!Enum.IsDefined(vehicleType))
        {
            throw new ArgumentOutOfRangeException(nameof(vehicleType));
        }

        Id = Guid.CreateVersion7();
        RegistrationNumber = normalizedRegistration;
        Make = make.Trim();
        Model = model.Trim();
        Year = year;
        VehicleType = vehicleType;
        DailyRate = dailyRate;
        AvailabilityStatus = VehicleAvailabilityStatus.Available;
    }

    /// <summary>
    /// The form in which registration numbers are stored and compared: trimmed and upper-case.
    /// Use it on user input before looking a vehicle up.
    /// </summary>
    public static string NormalizeRegistrationNumber(string? value) =>
        (value ?? "").Trim().ToUpperInvariant();

    /// <exception cref="InvalidOperationException">The vehicle is already rented.</exception>
    public void MarkAsRented()
    {
        if (AvailabilityStatus != VehicleAvailabilityStatus.Available)
        {
            throw new InvalidOperationException($"Vehicle '{RegistrationNumber}' is not available.");
        }

        AvailabilityStatus = VehicleAvailabilityStatus.Rented;
    }

    /// <exception cref="InvalidOperationException">The vehicle is not currently rented.</exception>
    public void MarkAsAvailable()
    {
        if (AvailabilityStatus != VehicleAvailabilityStatus.Rented)
        {
            throw new InvalidOperationException($"Vehicle '{RegistrationNumber}' is not currently rented.");
        }

        AvailabilityStatus = VehicleAvailabilityStatus.Available;
    }
}
