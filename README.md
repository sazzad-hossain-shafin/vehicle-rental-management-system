# Vehicle Rental Management System

## Overview

This project began as an object-oriented programming exercise and is now being independently redesigned and extended into a production-style vehicle rental management platform. It is an active portfolio project, developed in small, reviewable phases.

## Current Version

A layered .NET solution with a domain model, an application layer, PostgreSQL persistence through EF Core, automated tests, and a temporary console client. Data is stored in PostgreSQL, so it survives restarts. The console client needs a database to run (see [Running Locally](#running-locally)).

On first run into an empty fleet, the console adds three fictional sample vehicles:

| Registration | Vehicle | Type | Daily rate |
|--------------|---------|------|-----------|
| ABC-123 | Toyota Corolla (2022) | Car | $60 |
| DEF-456 | Honda CB500 (2021) | Motorcycle | $40 |
| GHI-789 | Toyota HiAce (2021) | Van | $90 |

## Current Features

Through a text menu, the console client can:

- List the vehicles that are currently available
- Search vehicles by type (Car, Motorcycle, Van)
- Filter vehicles by maximum daily rate
- Rent a vehicle for a number of days and print a rental summary
- Return a rented vehicle
- Show the rental history

**Domain layer** (`VehicleRental.Domain`)

- Validated entities: vehicles, customers and rentals reject invalid data.
- Two kinds of identifier: a permanent generated `Guid` for each entity, plus a unique business key (registration number for vehicles, customer number for customers).
- Separate state models: vehicle availability (`Available`, `Rented`) is tracked apart from the rental lifecycle (`Active`, `Completed`), and invalid transitions are rejected.
- Rental dates: a rental is billed per calendar day, counting the start day but not the return day, with a minimum of one day.
- Stable pricing history: the agreed price is calculated when a rental starts and stored on the rental.
- Strategy-based pricing: normal pricing, a 10% promotional discount and a 20% long-term discount (rentals of 7 days or more). A pricing policy decides which one applies, and the long-term discount always takes priority over the promotion.

**Application layer** (`VehicleRental.Application`)

- Use-case services for vehicles, customers and rentals, returning read-only DTOs instead of domain entities.
- Repository and unit-of-work abstractions, all asynchronous with cancellation support.
- Customer reuse: renting with a known customer number reuses that customer. The same number with a different name is rejected rather than overwriting the stored name.
- Clear error types for not-found and conflict cases.

**Infrastructure layer** (`VehicleRental.Infrastructure`)

- EF Core with PostgreSQL (Npgsql), configured with the Fluent API.
- Repository and unit-of-work implementations. Searches are filtered in SQL, and read-only queries are not tracked.
- Unique business keys, foreign keys that never cascade-delete rental history, CHECK constraints and explicit decimal precision.
- Optimistic concurrency (PostgreSQL row version) plus a database rule allowing only one active rental per vehicle, so two requests cannot both rent the same vehicle.
- A versioned `InitialCreate` migration. Migrations are applied explicitly; the app never changes the schema on its own.

See [ADR 001](docs/architecture/001-postgresql-persistence.md) for the reasoning behind these choices.

## Current Architecture

- C# on .NET 10, EF Core 10, PostgreSQL
- Strategy Pattern for pricing (`IVehiclePricingStrategy` and its implementations)
- Dependencies point inward:

```
VehicleRental.Console --> VehicleRental.Application --> VehicleRental.Domain
        |                          ^                          ^
        v                          |                          |
        +-------> VehicleRental.Infrastructure ---------------+
                          |
                          v
                     PostgreSQL
```

| Project | Role |
|---------|------|
| `src/VehicleRental.Domain` | Entities, enums and pricing rules. No framework dependencies. |
| `src/VehicleRental.Application` | Use cases, repository abstractions and DTOs. No database dependencies. |
| `src/VehicleRental.Infrastructure` | EF Core, PostgreSQL mapping, repositories, migrations. |
| `src/VehicleRental.Console` | Temporary text-based client and composition root. |
| `tests/VehicleRental.Domain.Tests` | Unit tests for the domain. |
| `tests/VehicleRental.Application.Tests` | Unit tests for the use cases, using in-memory fakes. |
| `tests/VehicleRental.Infrastructure.IntegrationTests` | Mapping and migration tests, plus PostgreSQL integration tests. |

## Development Roadmap

Planned work, none of which exists yet:

- ASP.NET Core Web API
- Authentication and authorization
- Booking and availability checking
- Docker and CI
- Frontend (later)

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, to run the app or the database tests, a PostgreSQL server (version 13 or later).

```bash
dotnet build VehicleRentalManagementSystem.slnx
dotnet test VehicleRentalManagementSystem.slnx
```

Building and testing need no database: the database tests are reported as skipped until you configure one (see below).

### Database setup

1. Create an empty database and a login for it on your PostgreSQL server, for example `vehiclerental`.
2. Store the connection string outside the repository. User secrets are the simplest way:

   ```bash
   dotnet user-secrets set "ConnectionStrings:VehicleRentalDatabase" \
     "Host=localhost;Port=5432;Database=vehiclerental;Username=<user>;Password=<password>" \
     --project src/VehicleRental.Console
   ```

   You can instead set the `ConnectionStrings__VehicleRentalDatabase` environment variable, or create a git-ignored `src/VehicleRental.Console/appsettings.Local.json` based on `appsettings.example.json`. Never commit real credentials.

3. Apply the migrations (the EF Core tool is pinned in `dotnet-tools.json`):

   ```bash
   dotnet tool restore
   # set ConnectionStrings__VehicleRentalDatabase to the same connection string first
   dotnet ef database update --project src/VehicleRental.Infrastructure
   ```

4. Run the console client:

   ```bash
   dotnet run --project src/VehicleRental.Console
   ```

To add a migration after changing the model:

```bash
dotnet ef migrations add <Name> --project src/VehicleRental.Infrastructure --output-dir Persistence/Migrations
```

### Database integration tests

The PostgreSQL tests create their own temporary database, apply the migrations to it, and drop it afterwards, so they never touch your application database. Point them at a server where your login may create databases:

```bash
# bash
export VEHICLERENTAL_TEST_CONNECTION="Host=localhost;Port=5432;Username=<user>;Password=<password>"
# PowerShell
$env:VEHICLERENTAL_TEST_CONNECTION = "Host=localhost;Port=5432;Username=<user>;Password=<password>"

dotnet test tests/VehicleRental.Infrastructure.IntegrationTests
```

Without that variable, those tests are skipped and the rest of the suite still runs.

## Project Status

Active portfolio redevelopment. The application is still a console prototype, now backed by PostgreSQL. There is no web API, authentication or frontend yet.

## License

Released under the [MIT License](LICENSE).
