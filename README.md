# Vehicle Rental Management System

## Overview

This project began as an object-oriented programming exercise and is now being independently redesigned and extended into a production-style vehicle rental management platform. It is an active portfolio project, developed in small, reviewable phases.

## Current Version

The repository currently contains the original console application, relocated into a standard solution layout. Its behaviour is unchanged. It keeps all data in memory, so nothing is saved between runs, and it starts with three sample vehicles:

| ID | Vehicle | Type | Daily rate |
|----|---------|------|-----------|
| 1 | Toyota Corolla | Car | $60 |
| 2 | Honda CB500 | Motorcycle | $40 |
| 3 | Toyota HiAce | Van | $90 |

## Current Features

Through a text menu, the application can:

- List the vehicles that are currently available
- Search vehicles by type (Car, Motorcycle, Van)
- Filter vehicles by maximum daily rate
- Rent a vehicle for a number of days and print a rental summary
- Return a rented vehicle
- Show the rental history for the current session
- Apply a pricing strategy to each rental:
  - Normal pricing
  - Optional 10% promotional discount, for rentals under 7 days
  - Automatic 20% long-term discount, for rentals of 7 days or more

## Current Architecture

- C# on .NET 10, as a console application
- Strategy Pattern for rental pricing (`IVehiclePricingStrategy` and three implementations)
- Simple Factory (`VehicleFactory`) for creating vehicles
- Single project: `src/VehicleRental.Console`

This codebase has known design limitations, which are being addressed in the phases below.

## Development Roadmap

Planned work, none of which exists yet:

- Layered architecture (domain, application, infrastructure, API)
- Improved domain model, with validation and rental dates
- Automated testing
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
dotnet run --project src/VehicleRental.Console
```

## Project Status

Active portfolio redevelopment. The application is a working console prototype, and the platform described in the roadmap is not built yet.

## License

Released under the [MIT License](LICENSE).
