# Vehicle Rental Management System

## Overview

This project began as an object-oriented programming exercise and is now being independently redesigned and extended into a production-style vehicle rental management platform. It is an active portfolio project, developed in small, reviewable phases.

## Current Version

The repository contains a dedicated domain library, a unit test project and a temporary console client that exercises the domain. The console application keeps all data in memory, so nothing is saved between runs. It starts with three sample vehicles:

| ID | Vehicle | Type | Daily rate |
|----|---------|------|-----------|
| 1 | Toyota Corolla (2022) | Car | $60 |
| 2 | Honda CB500 (2021) | Motorcycle | $40 |
| 3 | Toyota HiAce (2021) | Van | $90 |

## Current Features

Through a text menu, the console client can:

- List the vehicles that are currently available
- Search vehicles by type (Car, Motorcycle, Van)
- Filter vehicles by maximum daily rate
- Rent a vehicle for a number of days and print a rental summary
- Return a rented vehicle
- Show the rental history for the current session

The domain library provides:

- **Validated entities.** Vehicles, customers and rentals reject invalid data (empty IDs, non-positive rates, unreasonable years, invalid dates).
- **Separate state models.** Vehicle availability (`Available`, `Rented`) is tracked apart from the rental lifecycle (`Active`, `Completed`), and invalid transitions are rejected.
- **Rental dates.** Each rental has a start date, an expected return date and an actual return date. A rental is billed per calendar day, counting the start day but not the return day, with a minimum of one day.
- **Stable pricing history.** The agreed price is calculated when a rental starts and stored on the rental, so later pricing changes never alter past rentals.
- **Strategy-based pricing.** Normal pricing, a 10% promotional discount and a 20% long-term discount (rentals of 7 days or more). A pricing policy decides which one applies, and the long-term discount always takes priority over the promotion.

## Current Architecture

- C# on .NET 10
- `VehicleRental.Domain`: entities, enums and pricing, with no dependency on the console or any framework
- `VehicleRental.Console`: a temporary console client that references the domain
- `VehicleRental.Domain.Tests`: xUnit tests for the domain
- Strategy Pattern for pricing (`IVehiclePricingStrategy` and its implementations)

```
VehicleRental.Console  -->  VehicleRental.Domain  <--  VehicleRental.Domain.Tests
```

## Development Roadmap

Planned work, none of which exists yet:

- Application layer (use cases)
- More automated testing, including integration tests
- EF Core persistence
- ASP.NET Core Web API
- Authentication and authorization
- Booking and availability checking
- Docker and CI
- Frontend (later)

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build VehicleRentalManagementSystem.slnx
dotnet test VehicleRentalManagementSystem.slnx
dotnet run --project src/VehicleRental.Console
```

## Project Status

Active portfolio redevelopment. The application is still a console prototype on a redesigned domain model, and the platform described in the roadmap is not built yet.

## License

Released under the [MIT License](LICENSE).
