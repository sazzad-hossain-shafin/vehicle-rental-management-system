# Vehicle Rental Management System

## Overview

This project began as an object-oriented programming exercise and is now being independently redesigned and extended into a production-style vehicle rental management platform. It is an active portfolio project, developed in small, reviewable phases.

## Current Version

The repository contains a domain library, an application layer with use-case services, automated tests and a temporary console client. All data is held in memory, so nothing is saved between runs. The console starts with three sample vehicles:

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

**Domain layer** (`VehicleRental.Domain`)

- Validated entities: vehicles, customers and rentals reject invalid data.
- Separate state models: vehicle availability (`Available`, `Rented`) is tracked apart from the rental lifecycle (`Active`, `Completed`), and invalid transitions are rejected.
- Rental dates: a rental is billed per calendar day, counting the start day but not the return day, with a minimum of one day.
- Stable pricing history: the agreed price is calculated when a rental starts and stored on the rental.
- Strategy-based pricing: normal pricing, a 10% promotional discount and a 20% long-term discount (rentals of 7 days or more). A pricing policy decides which one applies, and the long-term discount always takes priority over the promotion.

**Application layer** (`VehicleRental.Application`)

- Use-case services for vehicles, customers and rentals, returning read-only DTOs instead of domain entities.
- Repository abstractions (`IVehicleRepository`, `ICustomerRepository`, `IRentalRepository`) and a unit-of-work abstraction, all asynchronous with cancellation support.
- Customer reuse: renting with a known customer ID reuses that customer. The same ID with a different name is rejected rather than overwriting the stored name.
- Unique vehicle IDs, and at most one active rental per vehicle.
- Clear error types for not-found and conflict cases.
- Temporary in-memory repositories, which will be replaced by a database.

## Current Architecture

- C# on .NET 10
- Strategy Pattern for pricing (`IVehiclePricingStrategy` and its implementations)
- Dependencies point inward:

```
VehicleRental.Console  -->  VehicleRental.Application  -->  VehicleRental.Domain
```

| Project | Role |
|---------|------|
| `src/VehicleRental.Domain` | Entities, enums and pricing rules. No framework dependencies. |
| `src/VehicleRental.Application` | Use cases, repository abstractions, DTOs, temporary in-memory repositories. |
| `src/VehicleRental.Console` | Temporary text-based client. Reads input and prints results only. |
| `tests/VehicleRental.Domain.Tests` | xUnit tests for the domain. |
| `tests/VehicleRental.Application.Tests` | xUnit tests for the application layer. |

## Development Roadmap

Planned work, none of which exists yet:

- EF Core persistence with a relational database
- ASP.NET Core Web API
- Authentication and authorization
- Booking and availability checking
- Integration tests
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

Active portfolio redevelopment. The application is still a console prototype, now built on separate domain and application layers. There is no database or web API yet.

## License

Released under the [MIT License](LICENSE).
