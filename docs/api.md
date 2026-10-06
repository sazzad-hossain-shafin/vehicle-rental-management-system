# API guide

A concise guide to the HTTP API. The generated OpenAPI document is the complete reference; this page covers the conventions it cannot express.

**Authentication is not implemented yet.** Every endpoint is open. Do not expose this API to an untrusted network.

## Running it

```bash
# one-time: store the connection string (also used by the console app) and create the schema
dotnet user-secrets set "ConnectionStrings:VehicleRentalDatabase" \
  "Host=localhost;Port=5432;Database=vehiclerental;Username=<user>;Password=<password>" \
  --project src/VehicleRental.Api
dotnet ef database update --project src/VehicleRental.Infrastructure   # needs the connection string in the environment

dotnet run --project src/VehicleRental.Api       # http://localhost:5270
```

- **Base URL:** `http://localhost:5270/api/v1` (the port comes from `launchSettings.json`; set `ASPNETCORE_URLS` to change it).
- **Configuration:** `ConnectionStrings:VehicleRentalDatabase`, from user secrets (development), environment variables (`ConnectionStrings__VehicleRentalDatabase`) or any other .NET configuration source. Nothing secret is stored in the repository.
- **Migrations are never applied automatically.** If the schema is missing or out of date, the API still starts, logs a warning, and `/health` reports 503 until you apply them.

## Documentation and health

| URL | What | Availability |
|-----|------|--------------|
| `/openapi/v1.json` | OpenAPI document | Development only |
| `/scalar/v1` | Interactive API reference | Development only |
| `/health` | Readiness: database reachable and migrated. 200 or 503 | Always |
| `/health/live` | Liveness: the process is running. Never touches the database | Always |

Health responses look like `{"status":"Healthy","checks":[{"name":"database","status":"Healthy","description":"..."}]}` and never contain connection details.

## Endpoints

IDs in URLs are permanent GUIDs. Business identifiers (registration number, customer number) have their own explicit routes.

| Method | Route | Purpose | Success |
|--------|-------|---------|---------|
| GET | `/vehicles` | List vehicles (paged). Filters: `vehicleType`, `maxDailyRate`, `availability` | 200 |
| GET | `/vehicles/{id}` | One vehicle | 200 |
| GET | `/vehicles/by-registration/{registrationNumber}` | One vehicle by plate | 200 |
| POST | `/vehicles` | Add a vehicle | 201 + `Location` |
| POST | `/customers` | Register a customer | 201 + `Location` |
| GET | `/customers/{id}` | One customer | 200 |
| GET | `/customers/by-number/{customerNumber}` | One customer by number | 200 |
| POST | `/rentals` | Start a rental today | 201 + `Location` |
| GET | `/rentals` | Rental history (paged), oldest start date first | 200 |
| GET | `/rentals/{id}` | One rental | 200 |
| POST | `/rentals/{id}/return` | Complete a rental | 200 |

Returning is an action (`POST .../return`) rather than a `PATCH` of the status, because completing a rental is a rule-bound state change: clients cannot set arbitrary states.

### Examples

Create a vehicle. The ID and availability are set by the system; clients cannot choose them.

```http
POST /api/v1/vehicles
{ "registrationNumber": "ABC-123", "make": "Toyota", "model": "Corolla",
  "year": 2022, "vehicleType": "Car", "dailyRate": 60 }
```
```json
{ "id": "01a110f2-8111-7ac3-a006-4a546d7b0c6d", "registrationNumber": "ABC-123",
  "displayName": "Toyota Corolla", "year": 2022, "vehicleType": "Car",
  "dailyRate": 60.00, "availabilityStatus": "Available" }
```

Start a rental for an existing vehicle and customer:

```http
POST /api/v1/rentals
{ "vehicleId": "01a110f2-8111-...", "customerId": "01a110f2-84f8-...",
  "rentalDays": 3, "promotionalDiscountRequested": false }
```
```json
{ "id": "...", "status": "Active", "startDate": "2026-10-06", "expectedReturnDate": "2026-10-09",
  "billableDays": 3, "dailyRateAtRental": 60.00, "pricingDescription": "Normal pricing",
  "totalCost": 180.00, "actualReturnDate": null, "...": "..." }
```

The total is calculated by the server when the rental starts and never changes afterwards. Rentals of 7 days or more get the long-term discount; the promotion applies only to shorter ones.

### Conventions

- **JSON:** camelCase names. Enums are text (`"Car"`, `"Active"`); numbers for enums are refused. Dates are `yyyy-MM-dd`.
- **Query enums** (`vehicleType`, `availability`) ignore letter case.
- **Business identifiers** are normalized (trimmed, upper-case), so `abc-123` finds `ABC-123`.

### Paging

List endpoints take `page` (from 1, default 1) and `pageSize` (1 to 100, default 20), and answer:

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 42, "totalPages": 3 }
```

A page past the end is empty, with the correct totals. Out-of-range values are a 400.

## Errors

Every error is [Problem Details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`) with `type`, `title`, `status` and a `traceId` you can quote to support.

| Status | When |
|--------|------|
| 400 | Malformed JSON, missing required fields (with an `errors` map per field), unknown enum text, invalid GUID or number, or a business rule such as a non-positive rate or a rental of zero days |
| 404 | The vehicle, customer or rental does not exist (including an unknown route) |
| 409 | A duplicate registration or customer number, a vehicle that is already rented, a rental that is already completed, or a request that lost a race with another one |
| 500 | Anything unexpected. The body is generic: no stack trace, SQL, connection details or file paths |

The 409 for a lost race is a real guarantee: two simultaneous requests can never both rent the same vehicle. See [ADR 001](architecture/001-postgresql-persistence.md).

## Not included yet

Authentication and authorization, rate limiting, CORS (no browser client exists yet), updating or deleting vehicles and customers, reservations/bookings, payments.
