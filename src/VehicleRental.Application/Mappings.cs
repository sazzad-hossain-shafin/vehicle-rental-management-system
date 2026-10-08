using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
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
            vehicle.RegistrationNumber,
            vehicle.Make,
            vehicle.Model,
            vehicle.DisplayName,
            vehicle.Year,
            vehicle.VehicleType,
            vehicle.DailyRate,
            vehicle.AvailabilityStatus);

    public static CustomerDto ToDto(this Customer customer) =>
        new(customer.Id, customer.CustomerNumber, customer.Name);

    public static ReservationDto ToDto(this Reservation reservation, DateOnly today) =>
        new(
            reservation.Id,
            reservation.Customer.Id,
            reservation.Customer.CustomerNumber,
            reservation.Customer.Name,
            reservation.Vehicle.Id,
            reservation.Vehicle.RegistrationNumber,
            reservation.Vehicle.DisplayName,
            reservation.Vehicle.VehicleType,
            reservation.StartDate,
            reservation.EndDate,
            reservation.Status,
            reservation.CreatedAt,
            reservation.CancelledAt,
            reservation.FulfilledAt,
            reservation.RentalId,
            reservation.DailyRateAtReservation,
            reservation.BillableDays,
            reservation.PricingDescription,
            reservation.TotalCost,
            reservation.IsExpired(today));

    public static RentalDto ToDto(this Rental rental) =>
        new(
            rental.Id,
            rental.Customer.Id,
            rental.Customer.CustomerNumber,
            rental.Customer.Name,
            rental.Vehicle.Id,
            rental.Vehicle.RegistrationNumber,
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
