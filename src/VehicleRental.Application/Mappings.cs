using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application;

/// <summary>
/// Converts domain entities to the read-only views the Application layer returns.
/// </summary>
internal static class Mappings
{
    public static VehicleDto ToDto(this Vehicle vehicle) =>
        new(
            vehicle.Id,
            vehicle.Make,
            vehicle.Model,
            vehicle.DisplayName,
            vehicle.Year,
            vehicle.VehicleType,
            vehicle.DailyRate,
            vehicle.AvailabilityStatus);

    public static CustomerDto ToDto(this Customer customer) =>
        new(customer.Id, customer.Name);

    public static RentalDto ToDto(this Rental rental) =>
        new(
            rental.Id,
            rental.Customer.Id,
            rental.Customer.Name,
            rental.Vehicle.Id,
            rental.Vehicle.DisplayName,
            rental.Vehicle.VehicleType,
            rental.StartDate,
            rental.ExpectedReturnDate,
            rental.ActualReturnDate,
            rental.Status,
            rental.DailyRateAtRental,
            rental.BillableDays,
            rental.PricingDescription,
            rental.TotalCost);
}
