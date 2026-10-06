# Vehicle Rental Management System

## Overview

This project began as an object-oriented programming exercise and is now being independently redesigned and extended into a production-style vehicle rental management platform. It is an active portfolio project, developed in small, reviewable phases.

## Current Version

A layered .NET solution with a domain model, an application layer, PostgreSQL persistence through EF Core, an ASP.NET Core Web API, automated tests, and a secondary console client. Data is stored in PostgreSQL, so it survives restarts. Both the API and the console client need a database to run (see [Running Locally](#running-locally)).

**Authentication and authorization** use ASP.NET Core Identity and short-lived JWT bearer tokens, with Admin, Staff and Customer roles. See [docs/authentication.md](docs/authentication.md).

## Current Features

**Web API** (`VehicleRental.Api`) &mdash; Minimal APIs under `/api/v1`, documented with OpenAPI. See [docs/api.md](docs/api.md). Browsing vehicles, signing in and registering a customer account are public; everything else needs a token and the right role.

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
| POST | `/auth/login`, `/auth/register` | Sign in; create a customer account |
| POST | `/admin/staff` | Create a staff account (Admin only) |
| GET | `/me`, `/me/customer`, `/me/rentals` | The signed-in account; a customer's own profile and rentals |

- Errors are RFC 9457 Problem Details, produced in one place; unexpected errors never leak internal detail.
- Authorization is by named policies, and every endpoint requires a sign-in unless it is explicitly marked anonymous, so a new endpoint can't be left open by accident.
- Customers can only reach their own data. The customer is taken from the signed token, never from an ID in the request, and another customer's rental looks exactly like one that doesn't exist.
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

- ASP.NET Core Identity for accounts, in the same database context as the business data, so a customer and their login are created in one transaction. Password hashing, validation and lockout (5 failures, 15 minutes) are Identity's; the domain knows nothing about Identity.
- JWT access tokens (HMAC-SHA256, 30 minutes) carrying only the account ID, roles and, for customers, the customer ID. The API refuses to start with a missing or weak signing key.

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
| `src/VehicleRental.Infrastructure` | EF Core, PostgreSQL mapping, repositories, migrations, health check, Identity accounts and token issuing. |
| `src/VehicleRental.Api` | HTTP endpoints, authentication and authorization, request validation, error handling, OpenAPI, health checks. |
| `src/VehicleRental.Console` | Secondary text-based client. |
| `tests/VehicleRental.Domain.Tests` | Unit tests for the domain. |
| `tests/VehicleRental.Application.Tests` | Unit tests for the use cases, using in-memory fakes. |
| `tests/VehicleRental.Infrastructure.IntegrationTests` | Mapping and migration tests, plus PostgreSQL integration tests. |
| `tests/VehicleRental.Api.Tests` | Tests of the real HTTP pipeline, some against PostgreSQL. |

## Development Roadmap

Planned work, none of which exists yet:

- Refresh tokens, email verification and password reset
- Booking and availability checking
- CI
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

### Configure sign-in secrets

The API will not start without a token signing key. Create a random one and keep it in user secrets (or the `Jwt__SigningKey` environment variable), never in the repository:

```bash
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/VehicleRental.Api
# PowerShell: $b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

Optionally create a first admin account at startup (needed to create staff accounts; the password must be strong):

```bash
dotnet user-secrets set "Seed:Admin:Email" "admin@example.test" --project src/VehicleRental.Api
dotnet user-secrets set "Seed:Admin:Password" "<a strong password>" --project src/VehicleRental.Api
```

### Run the API

```bash
dotnet run --project src/VehicleRental.Api
```

Sign in with `POST /api/v1/auth/login`, then send `Authorization: Bearer <accessToken>`. In the interactive reference, use **Authorize** and paste the token.

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

## Running with Docker

Docker Compose starts PostgreSQL, applies the database migrations and runs the API with one command, with no .NET SDK or database installed. It is an alternative to the local workflow above, not a replacement. This is a local development setup: there is no TLS, reverse proxy or cloud deployment. Details: [docs/docker.md](docs/docker.md).

```
Client --HTTP :8080--> api container --> Application / Domain / Infrastructure
                                              |
                                  postgres container --> named volume "pgdata"
              (migrate container: applies the schema once, then exits)
```

### Prerequisites

[Docker Desktop](https://www.docker.com/products/docker-desktop/) (or another engine) with Compose v2, **with the engine running**.

### Quick start

```bash
# 1. Create .env with random secrets (never committed)
powershell -File scripts/init-env.ps1     # Windows
bash scripts/init-env.sh                  # Linux, macOS, Git Bash

# 2. Build and start PostgreSQL, the migration job and the API
docker compose up --build -d

# 3. Check
docker compose ps
curl http://localhost:8080/health
```

Then open <http://localhost:8080/scalar/v1> for the interactive API reference (`/openapi/v1.json` for the raw document), sign in with the `ADMIN_EMAIL` and `ADMIN_PASSWORD` from your `.env`, and use **Authorize** to paste the token. `bash scripts/smoke-test.sh` exercises the running stack end to end.

### Environment variables

Set in `.env` (copy `.env.example`, or run the init script). **`POSTGRES_PASSWORD` and `JWT_SIGNING_KEY` are required and have no default**: Compose refuses to start without them, and the API still refuses weak signing keys. `ADMIN_EMAIL` and `ADMIN_PASSWORD` optionally create the first admin on first start. Optional: `POSTGRES_DB`, `POSTGRES_USER`, `JWT_ISSUER`, `JWT_AUDIENCE`, `ASPNETCORE_ENVIRONMENT` (`Development` by default, which serves the API docs; `Production` hides them), `API_PORT`, `POSTGRES_HOST_PORT`. Never commit `.env`.

### Database migration

The API never changes the schema on its own. A separate one-shot `migrate` service applies the EF Core migrations after PostgreSQL is healthy and before the API starts, and it only applies what is missing, so it is safe on every start. Run it by itself with `docker compose run --rm migrate`. If it fails, the API does not start.

### Starting and stopping

```bash
docker compose up --build -d     # start (rebuild after code changes)
docker compose logs -f api       # follow the API logs
docker compose stop              # stop, keeping containers
docker compose down              # stop and remove containers. Your data is KEPT (named volume)
```

### Resetting the local database

```bash
docker compose down -v           # DESTRUCTIVE: also deletes the database volume
```

`down -v` permanently deletes every vehicle, customer, rental and account in your local database. Plain `docker compose down` does not.

### Health checks and troubleshooting

`docker compose ps` shows `healthy` for `postgres` (`pg_isready`) and `api` (`/health`: database reachable and migrated). `/health/live` only reports that the process is running. For common problems (missing secrets, a stopped engine, port conflicts, password changes) see [docs/docker.md](docs/docker.md#troubleshooting).

## Project Status

Active portfolio redevelopment. The platform has a working, tested, authenticated HTTP API on PostgreSQL. It has a Docker Compose development environment, but no frontend or CI yet.

## License

Released under the [MIT License](LICENSE).

