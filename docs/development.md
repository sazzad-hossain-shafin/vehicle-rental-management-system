# Local development without Docker

The recommended way to run the project is [Docker Compose](docker.md). Use this page when you want to run the API, console client or tests directly with the .NET SDK.

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, to run the API, the console client or the database tests, a PostgreSQL server (the project is developed and tested against PostgreSQL 17).

```bash
dotnet build VehicleRentalManagementSystem.slnx
dotnet test VehicleRentalManagementSystem.slnx
```

Building and testing need no database: tests that need one are reported as skipped until you configure one (see below).

## Database setup

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

## Configure sign-in secrets

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

## Run the API

```bash
dotnet run --project src/VehicleRental.Api
```

Sign in with `POST /api/v1/auth/login`, then send `Authorization: Bearer <accessToken>`. In the interactive reference, use **Authorize** and paste the token.

It listens on `http://localhost:5270` (change with `ASPNETCORE_URLS`). In Development, open <http://localhost:5270/scalar/v1> for the interactive API reference, or fetch `/openapi/v1.json`. Check `http://localhost:5270/health` to confirm the database is ready. If the schema is not migrated, the API still starts, logs a warning, and `/health` answers 503.

## Run the console client

```bash
dotnet run --project src/VehicleRental.Console
```

## Database integration tests

The PostgreSQL tests (in the infrastructure and API test projects) create their own temporary databases, apply the migrations to them, and drop them afterwards, so they never touch your application database. Point them at a server where your login may create databases:

```bash
# bash
export VEHICLERENTAL_TEST_CONNECTION="Host=localhost;Port=5432;Username=<user>;Password=<password>"
# PowerShell
$env:VEHICLERENTAL_TEST_CONNECTION = "Host=localhost;Port=5432;Username=<user>;Password=<password>"

dotnet test VehicleRentalManagementSystem.slnx
```

Without that variable, those tests are skipped and the rest of the suite still runs.
