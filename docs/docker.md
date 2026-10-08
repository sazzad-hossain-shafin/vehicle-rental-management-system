# Running with Docker

A reproducible local environment: PostgreSQL and the API, started with Docker Compose. It is an addition to, not a replacement for, running the .NET projects directly (see the README).

This is a **local development setup**. It is not a production deployment: there is no TLS, no reverse proxy and no cloud configuration.

```
Client (browser, curl, a future frontend)
        │  HTTP, localhost:8080
        ▼
┌──────────────────────── Docker Compose ────────────────────────┐
│  api container (ASP.NET Core, non-root, port 8080)               │
│    API ─► Application ─► Domain                                   │
│    └────► Infrastructure (EF Core, Identity, JWT)                 │
│                │  Npgsql, "postgres:5432" on the Compose network  │
│                ▼                                                  │
│  postgres container (PostgreSQL 17) ──► named volume "pgdata"     │
│                                                                   │
│  migrate container (one-shot): applies the schema, then exits     │
└───────────────────────────────────────────────────────────────────┘
```

## Prerequisites

- Docker Desktop (or another Docker engine) with Compose v2, **and its engine running**.
- Nothing else: the .NET SDK is only needed if you also want to build and test outside Docker.

## Quick start

```bash
# 1. Create .env with random secrets (never committed). Either:
pwsh scripts/init-env.ps1      # or: powershell -File scripts/init-env.ps1
bash scripts/init-env.sh       # Linux, macOS, Git Bash
# ...or copy .env.example to .env and fill in POSTGRES_PASSWORD, JWT_SIGNING_KEY (and optionally ADMIN_*) by hand.

# 2. Build and start everything: PostgreSQL, then the migration job, then the API
docker compose up --build -d

# 3. Wait until it reports healthy, then check
docker compose ps
curl http://localhost:8080/health
```

Then open <http://localhost:8080/scalar/v1> (interactive reference) or <http://localhost:8080/openapi/v1.json>. Sign in with `POST /api/v1/auth/login` using `ADMIN_EMAIL` and `ADMIN_PASSWORD` from your `.env`, and paste the returned token into the reference's Authorize box.

To check the whole stack with real requests, run `bash scripts/smoke-test.sh`.

## Services

| Service | Image | Purpose | Host port |
|---------|-------|---------|-----------|
| `postgres` | `postgres:17` | The database. Data lives in the `pgdata` volume | `127.0.0.1:5432` |
| `migrate` | built from `Dockerfile` (target `migrate`) | One-shot job: applies the EF Core migrations, then exits | none |
| `api` | built from `Dockerfile` (target `api`) | The web API | `127.0.0.1:8080` |

Both published ports bind to localhost only, so the stack is not reachable from other machines. Change them with `API_PORT` and `POSTGRES_HOST_PORT` in `.env` if the defaults are taken.

## Configuration (`.env`)

| Variable | Required | Meaning |
|----------|----------|---------|
| `POSTGRES_PASSWORD` | **Yes** | Database password. Use letters and digits only (it goes inside a connection string) |
| `JWT_SIGNING_KEY` | **Yes** | Token signing secret, at least 32 random characters. The API refuses weak keys |
| `POSTGRES_DB`, `POSTGRES_USER` | No (`vehiclerental`) | Database and login names |
| `JWT_ISSUER`, `JWT_AUDIENCE` | No | Token issuer and audience |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | No | Creates an admin account on first start when **both** are set. Leave empty for none |
| `ASPNETCORE_ENVIRONMENT` | No (`Development`) | `Development` serves the OpenAPI document and interactive reference; `Production` hides them |
| `API_PORT`, `POSTGRES_HOST_PORT` | No | Host ports |

Rules the setup follows:

- **No secret is in the repository.** `compose.yaml` only references variables. `.env` is git-ignored and excluded from the Docker build context, so it cannot end up in an image.
- **Required secrets have no default.** If `POSTGRES_PASSWORD` or `JWT_SIGNING_KEY` is missing, Compose stops with a message naming it. There is no generated or example key to fall back on, and the API's own checks (a strong key, an issuer and audience) are unchanged.
- The API reads its configuration from environment variables (`ConnectionStrings__VehicleRentalDatabase`, `Jwt__SigningKey`, `Seed__Admin__Email`, ...). The database host is the Compose service name, `postgres`, not `localhost`.

## The website

The `web` service builds `frontend/` (Node 24) and serves it with an unprivileged nginx on <http://localhost:8081> (`WEB_PORT`). nginx forwards `/api/` to the API, sets a strict Content-Security-Policy and other security headers, and the container runs read-only with all capabilities dropped, like the API. It starts after the API is healthy. The API stays published on its own port, so API-only use is unchanged.

## Database migrations

The API **never changes the database schema itself**. A separate one-shot service does:

- `migrate` is built from the same repository with the EF Core tooling, which produces a **migrations bundle**: one self-contained executable holding every migration. The final `migrate` image contains only that bundle on a minimal base image. The API image has no EF tooling, SDK or migration code path.
- `docker compose up` runs it after PostgreSQL is healthy and before the API starts (`depends_on: service_completed_successfully`). It applies only migrations that are missing, so it is safe on every start, and it prints what it applied.
- Run it on its own at any time:

  ```bash
  docker compose run --rm migrate
  ```

- If a migration fails, `migrate` exits with an error and the API does not start, so you never run against a half-migrated schema.
- The reservations migration runs `CREATE EXTENSION IF NOT EXISTS btree_gist` (needed for the no-double-booking constraint). The Compose database user is the PostgreSQL superuser, so this works out of the box; the extension is also marked trusted, so a normal database owner can create it.
- To start only the API and skip the migration step (you accept the schema as it is): `docker compose up -d --no-deps api`.

## Health checks

| Check | How | Meaning |
|-------|-----|---------|
| `postgres` | `pg_isready` for the configured database | Accepting connections, not just "process exists" |
| `api` | `GET /health` from inside the container | Readiness: the database is reachable **and** migrated |
| (manual) | `GET /health/live` | Liveness only: the process is running; never touches the database |

`docker compose ps` shows `healthy` for both when the stack is ready. Health responses never include connection details.

## Day-to-day commands

```bash
docker compose up --build -d      # start (rebuilds images after code changes)
docker compose logs -f api        # follow the API log (stdout only; no log files)
docker compose ps                 # status and health
docker compose stop               # stop, keeping containers and data
docker compose down               # stop and remove containers. YOUR DATA IS KEPT (named volume)
```

### Resetting the local database

```bash
docker compose down -v            # DESTRUCTIVE
```

`-v` also deletes the `pgdata` volume: **every vehicle, customer, rental and account in your local database is permanently lost**. The next `docker compose up` starts from an empty database, applies the migrations and recreates the admin from `.env`. Plain `docker compose down` never deletes data.

## The image

- **Multi-stage build:** restore (cached until a project file changes) → publish → a small runtime image. The final API image contains the published output only: no SDK, no source, no tests, no `.git`, no `.env`.
- **Build context:** the repository root, filtered by an allow-list `.dockerignore` (only `src/` and the tool manifest are sent to the build).
- **Runs as a non-root user** (`app`, the user the official .NET images provide), with a read-only filesystem (`/tmp` is a memory-backed temporary directory), all Linux capabilities dropped and privilege escalation disabled.
- **Plain HTTP on 8080** inside the container. TLS belongs to whatever fronts the container.
- **Logs** go to stdout/stderr only. They contain no passwords, keys or tokens.
- **Shutdown:** the .NET host is the container's main process, so `docker stop` (SIGTERM) shuts it down cleanly and the exit code is 0.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| `required variable POSTGRES_PASSWORD is missing a value` | No `.env`, or the secret is empty. Run the init script or fill in `.env` |
| `api` exits right away; log mentions `Jwt:SigningKey` | The key is missing, shorter than 32 characters or too repetitive. Set a random `JWT_SIGNING_KEY` |
| `Cannot connect to the Docker daemon` / `dockerDesktopLinuxEngine ... not found` | The Docker engine is not running. Start Docker Desktop |
| `port is already allocated` | Another program uses 8080 or 5432. Set `API_PORT` / `POSTGRES_HOST_PORT` in `.env` |
| `/health` returns 503 | The database is unreachable or not migrated. Run `docker compose run --rm migrate` and check `docker compose logs postgres` |
| `password authentication failed` after changing `POSTGRES_PASSWORD` | PostgreSQL only reads the password when it first creates the volume. Either change it back, or reset the volume with `docker compose down -v` (destructive) |
| Admin cannot sign in | Admin creation happens once. If you changed `ADMIN_PASSWORD` later, the existing account is left unchanged by design; reset the database or use another account |
| `migrate` fails with an authentication or connection error | Same password issue as above, or PostgreSQL is not healthy yet: check `docker compose ps` |
| Windows: the shell scripts fail with odd characters | They need LF line endings; the repository's `.gitattributes` enforces this on checkout |

## Not included

TLS or a reverse proxy, a production configuration, secrets management beyond environment variables, image publishing, CI, and cloud or Kubernetes deployment.
