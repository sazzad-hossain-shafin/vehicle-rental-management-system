# API guide

A concise guide to the HTTP API. The generated OpenAPI document is the complete reference; this page covers the conventions it cannot express.

Most endpoints need a signed-in account (a JWT bearer token), and what you may do depends on your role: Admin, Staff or Customer. Browsing vehicles, signing in, registering a customer account and the health checks are public. See [authentication](authentication.md) for the full access matrix, roles and customer-ownership rules.

## Running it

The quickest way is Docker Compose, which starts PostgreSQL, applies the migrations and runs the API: see [docker.md](docker.md). To run it directly with the .NET SDK instead:

```bash
# one-time: store the connection string (also used by the console app) and create the schema
dotnet user-secrets set "ConnectionStrings:VehicleRentalDatabase" \
  "Host=localhost;Port=5432;Database=vehiclerental;Username=<user>;Password=<password>" \
  --project src/VehicleRental.Api
dotnet ef database update --project src/VehicleRental.Infrastructure   # needs the connection string in the environment

# the API refuses to start without a token signing key (a random secret, never committed)
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/VehicleRental.Api
# optional: create the first admin at startup
dotnet user-secrets set "Seed:Admin:Email" "admin@example.test" --project src/VehicleRental.Api
dotnet user-secrets set "Seed:Admin:Password" "<a strong password>" --project src/VehicleRental.Api

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

Reservations (booking a vehicle for future dates) are in the next section.

Access: vehicle reads are public; `POST /vehicles`, the customer endpoints and the rental desk endpoints (`POST /rentals`, `GET /rentals`, `POST .../return`) are for Staff and Admin; `GET /rentals/{id}` is for any signed-in account, but customers only get their own.

### Availability and reservations

A reservation holds a vehicle for the half-open date range `[startDate, endDate)` (free again on the end date) and stores a price quote. Dates are `yyyy-MM-dd`. The full behaviour, rules and limits are in [reservations](reservations.md).

| Method | Route | Access | Purpose | Success |
|--------|-------|--------|---------|---------|
| GET | `/vehicles/availability?startDate=&endDate=` | Public | Vehicles free for the period (paged; `vehicleType`, `maxDailyRate`) | 200 |
| GET | `/vehicles/{id}/quote?startDate=&endDate=` | Public | The price the pricing policy would quote, and whether the vehicle is free right now. Reserves nothing, and is not a guarantee | 200 |
| POST | `/me/reservations` | Customer | Reserve a vehicle for the signed-in customer (body: `vehicleId`, `startDate`, `endDate`) | 201 + `Location` |
| GET | `/me/reservations` | Customer | My reservations (paged) | 200 |
| GET | `/me/reservations/{id}` | Customer | One of my reservations (someone else's is a 404) | 200 |
| POST | `/me/reservations/{id}/cancel` | Customer | Cancel my reservation before its start date | 200 |
| POST | `/reservations` | Staff, Admin | Book at the desk for a customer (`customerId`, optional `promotionalDiscountRequested`) | 201 + `Location` |
| GET | `/reservations` | Staff, Admin | All reservations (paged; optional `status`) | 200 |
| GET | `/reservations/{id}` | Staff, Admin | One reservation | 200 |
| POST | `/reservations/{id}/cancel` | Staff, Admin | Cancel an active reservation | 200 |
| POST | `/reservations/{id}/pickup` | Staff, Admin | Hand the vehicle over: starts the rental and fulfils the reservation | 200 |

```http
POST /api/v1/me/reservations
{ "vehicleId": "01a110f2-8111-...", "startDate": "2026-11-10", "endDate": "2026-11-13" }
```
```json
{ "id": "...", "status": "Active", "startDate": "2026-11-10", "endDate": "2026-11-13",
  "billableDays": 3, "dailyRateAtReservation": 60.00, "pricingDescription": "Normal pricing",
  "totalCost": 180.00, "rentalId": null, "isExpired": false, "...": "..." }
```

The customer is never sent: it comes from the signed token. Overlapping an active reservation or rental, cancelling something that is not active, and picking up twice are 409s (as is losing a race to another booking of the same vehicle, which can simply be retried); invalid or past dates are 400s.

### Accounts and sign-in

Two ways to sign in. `POST /auth/login` returns the token in the body for API clients (send it as `Authorization: Bearer`). `POST /auth/session` is for browsers: it sets the same token as an HttpOnly, SameSite=Strict cookie scoped to `/api` and returns only `{ "expiresAtUtc", "user" }`; `DELETE /auth/session` clears it. Both session endpoints, and any state-changing request authenticated by the cookie, require the header `X-Requested-With: VehicleRentalWeb` (otherwise 403). See [authentication](authentication.md#browser-sessions).

| Method | Route | Access | Purpose | Success |
|--------|-------|--------|---------|---------|
| POST | `/auth/login` | Public | Exchange email and password for an access token | 200 |
| POST | `/auth/register` | Public | Create a customer account (and its customer record) | 201 |
| POST | `/admin/staff` | Admin | Create a staff account | 201 |
| GET | `/me` | Signed in | Who am I: id, email, roles, linked customer | 200 |
| GET | `/me/customer` | Customer | My own customer profile | 200 |
| GET | `/me/rentals` | Customer | My own rentals (paged) | 200 |

```http
POST /api/v1/auth/login
{ "email": "desk@example.test", "password": "..." }
```
```json
{ "accessToken": "<jwt>", "tokenType": "Bearer", "expiresAtUtc": "2026-10-06T11:30:00Z",
  "user": { "id": "...", "email": "desk@example.test", "roles": ["Staff"], "customerId": null, "customerNumber": null } }
```

Send the token as `Authorization: Bearer <accessToken>`. Tokens last 30 minutes; there are no refresh tokens yet. Registration has no role field: every registered account is a Customer.

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

A page past the end is empty, with the correct totals. The reservation lists use the same paging. Out-of-range values are a 400.

## Errors

Every error is [Problem Details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`) with `type`, `title`, `status` and a `traceId` you can quote to support.

| Status | When |
|--------|------|
| 400 | Malformed JSON, missing required fields (with an `errors` map per field), unknown enum text, invalid GUID or number, or a business rule such as a non-positive rate or a rental of zero days |
| 401 | No valid token (missing, malformed, expired or wrongly signed), or a failed login. Never says which |
| 403 | Signed in, but this role may not do this |
| 404 | The vehicle, customer or rental does not exist (including an unknown route). A customer asking for another customer's rental also gets 404 |
| 409 | A duplicate registration or customer number, a vehicle that is already rented, a rental that is already completed, or a request that lost a race with another one |
| 500 | Anything unexpected. The body is generic: no stack trace, SQL, connection details or file paths |

The 409 for a lost race is a real guarantee: two simultaneous requests can never both rent the same vehicle. See [ADR 001](architecture/001-postgresql-persistence.md).

## Not included yet

Refresh tokens, email verification and password reset, rate limiting, CORS (no browser client exists yet), updating or deleting vehicles and customers, payments, deposits and notifications for reservations.
