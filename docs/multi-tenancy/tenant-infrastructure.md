# Tenant infrastructure (Phase 14A-4)

Part of the [multi-company plan](README.md) ([ADR 007](../architecture/007-multi-company-tenancy.md)). Phase 14A-4 builds the machinery that will make a request act for exactly one company, proven by the [spike](../../tests/VehicleRental.TenantIsolation.Spike/README.md). **It is built, tested and switched off.** No production route binds a tenant, no production table has Row-Level Security, and `Tenancy:Enabled` is `false` by default, so everything the application does today behaves as before.

## What exists now, and what does not

| Exists (14A-4) | Does not exist yet |
|---|---|
| `ITenantContext` (scoped), `ITenantBinder`: bind once, never change | any production route that binds a tenant |
| company slug and membership lookup (`ICompanyDirectory`, `IMembershipDirectory`) | EF query filters (they would hide rows whose `CompanyId` is still NULL) |
| the transaction-local tenant setting, applied by an EF Core transaction interceptor | RLS policies on real tables (14A-7) |
| fail-closed command guard | the backfill of existing rows (14A-5) |
| `ITenantTransaction`, and the unit of work joining it | company registration, invitations, owner dashboard |
| endpoint filters `RequirePublicCompany`, `RequireCompanyMember`, `InTenantTransaction`, attached to nothing | authorization policies based on membership |
| `ITenantScopeRunner` for background work, unused | tenant-aware Console |
| the restricted runtime database role and the startup role check | |

## Why existing records with a NULL CompanyId are unaffected

Five independent facts, each tested:

1. **Nothing binds a tenant** on any existing code path, so every context is unbound and unbound contexts are not touched by the interceptors or the guard.
2. **`Tenancy:Enabled` is false**: binding throws `TenancyDisabledException`, so even a stray call changes nothing. Tenant routes answer 404 while it is off.
3. **No query filter** is added, so queries return exactly what they returned before.
4. **No RLS policy exists on a real table.** Policies appear only in disposable test databases (`preview-rls.sql`), where the tests use them to prove the design against the real tables and services.
5. The **unit of work's original code path** is untouched: it runs unchanged whenever no tenant is bound.

## The transaction architecture

For a tenant-scoped request the order is fixed and enforced:

1. **Bind** the tenant (from the slug of an active company, or from a verified membership). The company is never read from a body, query string, custom header or the token, and it cannot be rebound to a different company.
2. **Begin** the tenant transaction (`ITenantTransaction.BeginAsync`). It refuses to start without a bound tenant, with tenancy disabled, or when a transaction is already open.
3. The EF Core **transaction interceptor** applies the setting as the first statement inside that transaction: `set_config('app.company_id', @company, true)`. The `true` makes it **transaction-local**: PostgreSQL discards it at COMMIT or ROLLBACK on every path. It returns the value it set, which is checked against the bound company in the same round trip; a mismatch stops the request. If the interceptor were missing, `BeginAsync` notices the setting was not applied, rolls back and throws.
4. **Queries and commands** run. The command guard refuses any command for a tenant-bound context that is not in the transaction the setting was applied to, and any raw SQL that mentions the tenant setting, the role or row security.
5. The **unit of work joins** the transaction: `LockVehicleAsync` and `SaveChangesAsync` take the same vehicle row lock and save the same changes with the same conflict messages, but never begin, commit or discard a transaction. A failed statement aborts the PostgreSQL transaction, so nothing partial can commit.
6. **Commit** on success, or **roll back** on an exception or a 400-or-above answer (`InTenantTransaction`). A scope disposed without committing rolls back.

EF Core sends a single-statement `SaveChanges` with no transaction at all, so tenant contexts set `AutoTransactionBehavior.Always`; reads need the explicit transaction and are refused without it instead of returning an empty result.

Session-scoped PostgreSQL settings and the driver's connection reset are never relied on: tests run with the driver's reset **disabled**.

### Background and administrative work

`ITenantScopeRunner.RunAsync(company, work)` creates a scope, binds the company (`Source = Background`), begins the tenant transaction, runs the work and commits or rolls back. `ForEachActiveCompanyAsync` lists active companies and does the work once per company, each in its own scope and transaction. It is never "all companies at once". Nothing uses it yet.

## The database roles

| Role | Used by | Can |
|---|---|---|
| **owner** (`POSTGRES_USER`) | the migration job and the `db-roles` job | everything (owns the tables) |
| **runtime** (`POSTGRES_APP_USER`, default `vehiclerental_app`) | the API | SELECT, INSERT, UPDATE, DELETE on the tables; USAGE on sequences; SELECT only on the migration history |

The runtime role is created and kept in shape by `scripts/sql/provision-runtime-role.sql`, run by the Compose one-shot `db-roles` service after the migrations. It is idempotent and the same file the tests run. The role:

- is not a superuser and has no `CREATEDB`, `CREATEROLE`, `REPLICATION` or `BYPASSRLS`, and does not inherit;
- is a member of no other role (any membership is revoked on every run);
- owns nothing, and the script **stops** if it finds that it does;
- cannot create objects in the schema or the database, cannot truncate, and cannot change the migration history;
- gets the same privileges on tables and sequences created later (default privileges), so a new migration does not need a manual step. The script also covers tables created before the defaults existed;
- is never the owner: the script refuses to run if asked to make the owner (or `postgres`) the runtime role. It re-asserts every attribute on every run, so a role made more powerful by hand is brought back down.

The two passwords are separate secrets. The script reads the role name and password from custom settings (`PGOPTIONS` in Compose), never from a command line or from the file, and its error messages never contain the password.

### The startup role check

`Tenancy:DatabaseRoleCheck` decides what happens when the API's own role is unsafe (superuser, `BYPASSRLS`, can create roles or databases, replication, owns or is a member of the owner of application tables, can create objects, can change the migration history):

| Mode | Behaviour | Default for |
|---|---|---|
| `Off` | no check. **Refused outside the Development environment** (the API will not start) | nothing |
| `Warn` | logs each problem; `/health` stays ready but says so in its description. Outside Development only as a temporary emergency setting, and logged as one | the Development environment (`dotnet run`) when the setting is absent |
| `Enforce` | the API **refuses to start**; `/health` is unhealthy while the role cannot be confirmed safe | **every other environment** when the setting is absent; Docker Compose and CI set it explicitly |

When the setting is absent, the environment decides (`ASPNETCORE_ENVIRONMENT`): Development gets `Warn`, anything else (Production, Staging, a custom name) gets `Enforce`. A deployment that forgets the setting therefore cannot start as an unsafe role.

Messages say what is wrong and never include the role name, a password or a connection string. An unreachable database at startup is not a violation (the API already starts degraded); the readiness check keeps trying and, in `Enforce`, stays unhealthy until it can confirm the role is safe.

The part of the spike's check that asks "is RLS enabled and forced on the tenant tables" is deliberately absent: no production table has RLS yet. 14A-7 adds it.

## Configuration

| Setting | Default | Meaning |
|---|---|---|
| `Tenancy:Enabled` | `false` | master switch for tenant mode |
| `Tenancy:DatabaseRoleCheck` | not set: `Warn` in Development, `Enforce` elsewhere | `Off` (Development only), `Warn` or `Enforce` |
| `POSTGRES_APP_USER` | `vehiclerental_app` | runtime role name (Compose) |
| `POSTGRES_APP_PASSWORD` | none, required | runtime role password (Compose): letters and digits, at least 16 characters |

Existing `.env` files predate `POSTGRES_APP_PASSWORD`; see the [runbook](runtime-role-runbook.md) to add it without touching any other value.

## Tests that prove it

- **Lifecycle:** unbound by default, bind once, same company is a no-op, a different company is refused, concurrent binds leave one winner, disabled mode.
- **Resolution:** active slug found whatever the case; pending, suspended, unknown, malformed and reserved slugs all look the same (404); membership is the user's active role in that company only, and removal applies on the very next request; a suspended company cannot be managed, a pending one can.
- **Ordering and guards:** the setting is in the transaction before the first query and gone after commit (with the driver reset off); reads, writes and single-statement saves outside the transaction are refused; raw SQL touching the tenant is refused; a transaction opened before binding is never trusted.
- **Pooling and concurrency:** a pool of one connection serves two companies in turn (same backend process, no leak); 240 overlapping requests from three companies over six connections; alternating tenants; cancellation, rollback and a database error followed by reuse; missing, malformed, nil and hostile tenant values fail closed; cross-company read, update, delete, move, count, sum and lock find nothing.
- **Booking inside the tenant transaction:** reservations, overlap conflicts (same 409), back-to-back reservations, the reservation-versus-reservation and rental-versus-reservation races (exactly one winner, 15 rounds each), pickup, return, a second active rental refused, availability paging, and no cross-company access to vehicles, reservations or rentals.
- **Tenant mode off:** the whole booking flow, both races and registration and login work as the restricted role on the original unit-of-work path.
- **Runtime role:** attributes, ownership and membership, what it can and cannot do, idempotence, de-elevation, later tables, bad inputs, and the safety check detecting each unsafe privilege separately.
- **API:** the filters on test-only routes (404 for stranger, foreign ID, removed member and unknown company alike; 403 for staff on an owner route; company in a body, query or header ignored; commit on success, rollback on 400-or-above or an exception; 120 simultaneous requests never cross; tenancy off answers 404) and the startup check in each mode.
- **Existing suites as the runtime role:** the whole API suite (with the role check enforced) and the whole infrastructure suite pass connecting as the restricted role; CI runs the booking, concurrency and authentication classes that way on every build.

## Limits

- **PgBouncer in transaction-pooling mode is not verified** and must not be assumed supported. The setting is transaction-local, so it should work, but nothing here tests it.
- **RLS does not protect against arbitrary SQL execution by a compromised application role.** That role can set the same setting. The design defends against bugs (a forgotten filter, a wrong query). Compensating controls: parameterised EF queries, the command guard, review of any raw SQL; a role per company is the heavier upgrade path.
- Planner statistics and catalog row estimates are not tenant-filtered.
- The preview policies and the trigger that fills `CompanyId` exist only in test databases; they illustrate 14A-7 but are not it.
- The interplay with the Console client is unchanged in this phase: it keeps working as before and is not tenant-aware.

## What Phase 14A-5 will require

14A-5 is the backfill. It needs, in this order:

1. **A default company** created from operator-supplied configuration (name, slug, time zone), status Active, never invented in code.
2. **A repeatable, idempotent backfill** in small batches that sets `CompanyId` on every vehicle, reservation and rental, creates a `CompanyCustomers` link for every customer (keeping each number), sets `CompanyCustomerId`, and converts existing Staff users into memberships. See [expand and cutover](expand-and-cutover.md#3-backfill-and-cutover-design-for-the-later-steps).
3. **The write-gap guard**: a `BEFORE INSERT` trigger that fills a NULL `CompanyId` only while exactly one company exists and raises otherwise (never a column default).
4. **The verification query set** (no NULL tenant columns, company agreement across vehicle, customer link and row, counts equal to before, every customer number once) with its output attached to the pull request, and a **backup and migration rehearsal** on a copy of real data using the existing scripts.
5. **The runtime role stays in place**; the backfill itself runs as the owner (a migration or a one-shot job), never as the runtime role.
6. A **decision** on how the default company's identity is supplied (configuration keys and who runs the job), and on the Console client (it keeps inserting NULL-company rows until it is given a company, so the guard trigger must cover it).

Nothing in 14A-5 may enable tenancy, RLS or query filters: those arrive in 14A-6 and 14A-7 after the data is verified.
