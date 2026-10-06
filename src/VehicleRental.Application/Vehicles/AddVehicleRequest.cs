using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Vehicles;

public sealed record AddVehicleRequest(
    string RegistrationNumber,
    string Make,
    string Model,
    int Year,
    VehicleType VehicleType,
    decimal DailyRate);
