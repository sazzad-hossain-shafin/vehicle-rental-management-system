# Vehicle Rental Management System

## Overview

This project began as an object-oriented programming exercise and is now being independently redesigned and extended into a production-style vehicle rental management platform. It is an active portfolio project, developed in small, reviewable phases.

## Current Version

A layered .NET solution with a domain model, an application layer, PostgreSQL persistence through EF Core, an ASP.NET Core Web API, automated tests, and a secondary console client. Data is stored in PostgreSQL, so it survives restarts. Both the API and the console client need a database to run (see [Running Locally](#running-locally)).

**Authentication is not implemented yet.** The API is open and must not be exposed to an untrusted network.

## Current Features

**Web API** (`VehicleRental.Api`) &mdash; Minimal APIs under `/api/v1`, documented with OpenAPI. See [docs/api.md](docs/api.md).

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/vehicles` | List vehicles, paged, filtered by `vehicleType`, `maxDailyRate`, `availability` |
| GET | `/vehicles/{id}`, `/vehicles/by-registration/{registrationNumber}` | Get a vehicle |
| POST | `/vehicles` | Add a vehicle |
| POST | `/customers` | Register a customer |
| GET | `/customers/{id}`, `/customers/by-number/{customerNumber}` | Get a customer |
| POST | `/rentals` | Start a rental |
| GET | `/rentals`, `/rentals/{id}` | Rental history (paged), one rental |
| POST | `/rentals/{id}/return` | Complete a rental |

- Errors are RFC 9457 Problem Details, produced in one place; unexpected errors never leak internal detail.
- Health checks: `/health` (database reachable and migrated) and `/health/live`.
- OpenAPI document and interactive reference at `/openapi/v1.json` and `/scalar/v1`, in Development only.

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
- Database-side paging and filtering for vehicle and rental lists.
- Customer rules: renting by customer number reuses the customer, and the same number with a different name is rejected rather than overwriting the stored name.
- Clear error types for not-found and conflict cases.

**Infrastructure layer** (`VehicleRental.Infrastructure`)

- EF Core with PostgreSQL (Npgsql), configured with the Fluent API.
- Repository and unit-of-work implementations. Searches are filtered in SQL, and read-only queries are not tracked.
- Unique business keys, foreign keys that never cascade-delete rental history, CHECK constraints and explicit decimal precision.
- Optimistic concurrency (PostgreSQL row version) plus a database rule allowing only one active rental per vehicle, so two requests cannot both rent the same vehicle.
- A versioned `InitialCreate` migration. Migrations are applied explicitly; neither the API nor the console client changes the schema on its own.

**Console client** (`VehicleRental.Console`): a text menu for listing, searching, renting and returning vehicles and viewing the history. It remains as a secondary demo client.

See [ADR 001](docs/architecture/001-postgresql-persistence.md) for the reasoning behind the persistence choices.

## Current Architecture

- C# on .NET 10, ASP.NET Core Minimal APIs, EF Core 10, PostgreSQL
- Strategy Pattern for pricing (`IVehiclePricingStrategy` and its implementations)
- Built-in dependency injection; the API is the composition root
- Dependencies point inward:

```
                 +-- Console
                 |
Clients --HTTP--> API --> Application --> Domain
                 |           ^              ^
                 +--> Infrastructure -------+
                          |
                          v
                      PostgreSQL
```

| Project | Role |
|---------|------|
| `src/VehicleRental.Domain` | Entities, enums and pricing rules. No framework dependencies. |
| `src/VehicleRental.Application` | Use cases, repository abstractions and DTOs. No database or web dependencies. |
| `src/VehicleRental.Infrastructure` | EF Core, PostgreSQL mapping, repositories, migrations, health check. |
| `src/VehicleRental.Api` | HTTP endpoints, request validation, error handling, OpenAPI, health checks. |
| `src/VehicleRental.Console` | Secondary text-based client. |
| `tests/VehicleRental.Domain.Tests` | Unit tests for the domain. |
| `tests/VehicleRental.Application.Tests` | Unit tests for the use cases, using in-memory fakes. |
| `tests/VehicleRental.Infrastructure.IntegrationTests` | Mapping and migration tests, plus PostgreSQL integration tests. |
| `tests/VehicleRental.Api.Tests` | Tests of the real HTTP pipeline, some against PostgreSQL. |

## Development Roadmap

Planned work, none of which exists yet:

- Authentication and authorization
- Booking and availability checking
- Docker and CI
- Frontend (later)

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, to run the API, the console client or the database tests, a PostgreSQL server (version 13 or later).

```bash
dotnet build VehicleRentalManagementSystem.slnx
dotnet test VehicleRentalManagementSystem.slnx
```

Building and testing need no database: tests that need one are reported as skipped until you configure one (see below).

### Database setup

1. Create an empty database and a login for it on your PostgreSQL server, for example `vehiclerental`.
2. Store the connection string outside the repository. User secrets are the simplest way, and one command serves both the API and the console client:

   ```bash
   dotnet user-secrets set "ConnectionStrings:VehicleRentalDatabase" \
     "Host=localhost;Port=5432;Database=vehiclerental;Username=<user>;Password=<password>" \
     --project src/VehicleRental.Api
   ```

   You can instead set the `ConnectionStrings__VehicleRentalDatabase` environment variable, or (console only) create a git-ignored `src/VehicleRental.Console/appsettings.Local.json` based on `appsettings.example.json`. Never commit real credentials.

3. Apply the migrations (the EF Core tool is pinned in `dotnet-tools.json`):

   ```bash
   dotnet tool restore
   # set ConnectionStrings__VehicleRentalDatabase to the same connection string first
   dotnet ef database update --project src/VehicleRental.Infrastructure
   ```

To add a migration after changing the model:

```bash
dotnet ef migrations add <Name> --project src/VehicleRental.Infrastructure --output-dir Persistence/Migrations
```

### Run the API

```bash
dotnet run --project src/VehicleRental.Api
```

It listens on `http://localhost:5270` (change with `ASPNETCORE_URLS`). In Development, open <http://localhost:5270/scalar/v1> for the interactive API reference, or fetch `/openapi/v1.json`. Check `http://localhost:5270/health` to confirm the database is ready. If the schema is not migrated, the API still starts, logs a warning, and `/health` answers 503.

### Run the console client

```bash
dotnet run --project src/VehicleRental.Console
```

### Database integration tests

The PostgreSQL tests (in the infrastructure and API test projects) create their own temporary databases, apply the migrations to them, and drop them afterwards, so they never touch your application database. Point them at a server where your login may create databases:

```bash
# bash
export VEHICLERENTAL_TEST_CONNECTION="Host=localhost;Port=5432;Username=<user>;Password=<password>"
# PowerShell
$env:VEHICLERENTAL_TEST_CONNECTION = "Host=localhost;Port=5432;Username=<user>;Password=<password>"

dotnet test VehicleRentalManagementSystem.slnx
```

Without that variable, those tests are skipped and the rest of the suite still runs.

## Project Status

Active portfolio redevelopment. The platform has a working, tested HTTP API on PostgreSQL. It has no authentication, frontend, Docker setup or CI yet.

## License

Released under the [MIT License](LICENSE).
