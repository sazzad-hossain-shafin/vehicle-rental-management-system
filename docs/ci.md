# Continuous integration

The workflow in [.github/workflows/ci.yml](../.github/workflows/ci.yml) runs on every pull request and on every push to `main`. A newer run on the same branch cancels the older one. The workflow has read-only access to the repository (`permissions: contents: read`), uses no repository secrets, and publishes nothing.

## Jobs

| Job | Runs | Purpose |
|---|---|---|
| Quality checks | in parallel | Repository hygiene, formatting, NuGet vulnerabilities |
| Build and test | in parallel | Release build, all tests against PostgreSQL, skipped-test guard |
| Docker build and compose smoke test | after *Build and test* | Image builds, Compose stack, smoke test |
| Frontend checks | in parallel | Typecheck, lint, unit tests, build, npm audit |
| Frontend end-to-end | after *Frontend checks* | Playwright against the full stack |

All jobs run on `ubuntu-latest` with a timeout. Actions are pinned to full commit SHAs (the release is in a comment); Dependabot keeps them current.

## .NET setup

`actions/setup-dotnet` installs the latest .NET 10 SDK and caches NuGet packages, keyed on the project files. [global.json](../global.json) requires at least SDK 10.0.100 and refuses a different major version.

## Tests and PostgreSQL

The *Build and test* job starts a `postgres:17` service container with a health check and sets `VEHICLERENTAL_TEST_CONNECTION` to it. The credentials are throwaway values that exist only for that container. The database-backed tests create and drop their own uniquely named databases.

Without that variable the database tests are **skipped**, and `dotnet test` still exits successfully. To stop a lost connection from producing a green run, `scripts/ci/check-test-results.py` reads the TRX result files (not console text) and fails if there are no results, fewer result files than test projects, or any test that did not pass, including skipped ones. It does not hard-code a test count. The TRX files are uploaded as the `test-results` artifact.

## Docker verification

The *Docker* job generates a random `.env` with `scripts/init-env.sh` (values are masked in logs), validates and builds `compose.yaml` (the API and the migration image), starts the stack, and waits up to four minutes for PostgreSQL, the migration job and the API to be ready (`scripts/ci/wait-for-stack.sh`; it fails immediately if migration or the API exits). It then runs `scripts/smoke-test.sh`. On failure, container status and the last 300 log lines are uploaded as `compose-diagnostics`. Containers, networks and volumes are always removed.

## Frontend jobs

- **Frontend checks:** Node 24 (pinned by `frontend/.nvmrc`), `npm ci` from the lockfile, strict typecheck, ESLint, the Vitest tests, the production build, and `npm audit --audit-level=high` (high or critical advisories fail; this mirrors the NuGet policy).
- **Frontend end-to-end:** starts the full Compose stack (database, migrations, API and website) with throwaway secrets, installs Chromium, and runs the Playwright tests against it. On failure the Playwright report and container logs are uploaded. The stack and its volumes are always removed.

## Branch protection

The `Protect main` repository ruleset applies to `main` only. It blocks deletion and force pushes, requires changes to go through a pull request (no approving review is required, since there is a single maintainer; review threads must be resolved), and requires these checks to pass on a branch that is up to date with `main`:

- Quality checks
- Build and test
- Docker build and compose smoke test

There is no bypass actor, so even the owner uses pull requests. If CI itself is broken, the owner can still edit the ruleset in the repository settings. The workflow has no path filters, so every pull request reports all three checks. Action versions are updated by Dependabot pull requests that keep the full commit SHA pinning.

## Policies

- **Vulnerabilities**: `scripts/ci/check-vulnerabilities.py` uses `dotnet list package --vulnerable --include-transitive`. High and Critical findings fail the build; Moderate and Low are annotated as warnings; outdated packages never fail it. If the vulnerability data cannot be read, the check fails instead of passing silently.
- **Formatting**: only `dotnet format whitespace --verify-no-changes` is enforced. Style and analyzer rules are not, so the check will not demand large rewrites.
- **Repository hygiene**: `scripts/ci/check-repo-hygiene.sh` fails if env, key or certificate files are tracked, a private key or JWT-shaped token is present, `.env.example` holds secret values, an action is not SHA-pinned, or a workflow uses `pull_request_target`, repository secrets or write permissions. Personal identifiers are not scanned for in CI because listing them in a script would publish them; check those by hand before publishing.

## Running the checks locally

```bash
bash scripts/ci/check-repo-hygiene.sh
dotnet restore VehicleRentalManagementSystem.slnx
dotnet format whitespace VehicleRentalManagementSystem.slnx --verify-no-changes
python3 scripts/ci/check-vulnerabilities.py
dotnet build VehicleRentalManagementSystem.slnx -c Release --no-restore -warnaserror
# with VEHICLERENTAL_TEST_CONNECTION set to a PostgreSQL server (see the README)
dotnet test VehicleRentalManagementSystem.slnx -c Release --no-build --logger trx --results-directory TestResults
python3 scripts/ci/check-test-results.py TestResults tests
# Docker
bash scripts/init-env.sh && docker compose up -d --build
bash scripts/ci/wait-for-stack.sh && bash scripts/smoke-test.sh
docker compose down --volumes
```

## Limitations

- Hosted runs use `ubuntu-latest`, which GitHub is moving to a newer Ubuntu release in late October 2026; a future image change could need small fixes.
- The vulnerability check needs network access to NuGet's advisory data and can fail for outages (deliberately).
- The Docker job builds on one platform (linux/amd64) and publishes no images.
- There is no code coverage, static-analysis or end-to-end browser testing yet.
