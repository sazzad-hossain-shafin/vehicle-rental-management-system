# ADR 007: Multi-company tenancy

Status: accepted. The Row-Level Security part was conditional on a proof of concept; that spike succeeded (PR 14A-2, [results](../../tests/VehicleRental.TenantIsolation.Spike/README.md)), so the decision stands, with the production requirements recorded in [Spike outcome](#spike-outcome-and-production-requirements) below.

Nothing in this record changes how the system behaves today. It describes where the system is going and the rules the next changes must follow. The supporting documents are in [../multi-tenancy/](../multi-tenancy/README.md).

## Context

Today the system serves one rental business. There is one fleet, one customer list, and every staff account sees everything. The owner wants several independent rental companies to run on the same platform, each with its own owners, staff, fleet, reservations, rentals, settings and reports, while customers book from individual companies without any company (or other customer) seeing data it has no right to.

What the code base looks like now (checked against the source, not assumed):

- **No tenant column anywhere.** `Vehicles`, `Reservations`, `Rentals` and `Customers` have no company reference. `Vehicles.RegistrationNumber` and `Customers.CustomerNumber` are unique across the whole database.
- **Global roles.** `Admin`, `Staff` and `Customer` are Identity roles carried in a 30-minute stateless JWT (also sent as an HttpOnly cookie to the website). Every "staff" policy means "any staff or admin", so any staff member can read every reservation, rental and customer.
- **Strong booking guarantees that must survive** ([ADR 004](004-reservations.md), [ADR 005](005-vehicle-booking-lock.md)): a PostgreSQL exclusion constraint against overlapping active reservations, one active rental per vehicle, a `SELECT ... FOR UPDATE` lock on the vehicle row before an availability check, and `xmin` row versions. These are all per vehicle, so they stay correct once a vehicle belongs to exactly one company.
- **Server-local "today".** Date rules (cannot book in the past, cancel until the day before pickup, pickup window) use the server's clock, which is wrong for a company in another time zone.
- **Missing infrastructure:** no email sending or confirmation, no rate limiting, no audit log, and the application connects to PostgreSQL as the table owner.

## Decision

### 1. Isolation: one shared schema, three independent layers

Every tenant-owned table gets a `CompanyId`. Isolation is enforced three times, so a mistake in one layer is caught by another:

1. **EF Core global query filters** driven by a scoped tenant context. Every query is filtered by default, which is what makes the common case safe.
2. **PostgreSQL Row-Level Security** as the backstop. Policies compare `CompanyId` with a **transaction-local** setting and fail closed: with no setting there are no rows. The application connects as a role that does not own the tables and has no `BYPASSRLS`, so the policies cannot be skipped.
3. **Composite tenant-aware foreign keys**, for example `Reservations(CompanyId, VehicleId)` referencing `Vehicles(CompanyId, Id)`. The database itself then refuses a reservation that mixes two companies.

Schema-per-tenant and database-per-tenant were rejected: they multiply migrations, connection pools and backups for no benefit at this scale. Filters alone were rejected because one forgotten `IgnoreQueryFilters()` is a data breach. RLS alone was rejected because it is hard to debug and gives no application-level error messages.

The RLS setting must be **transaction-local** (`set_config(..., true)` / `SET LOCAL`), not session-scoped with a manual reset, so a connection returned to the pool by any path (success, exception, rollback, cancellation) cannot carry a tenant to the next request. PR 14A-2 proved this works with EF Core and Npgsql (see below).

### 2. Tenant resolution: never trust a browser-supplied company

- **Public and customer routes** (`/api/v1/c/{slug}/...`): the company comes from the slug and must be an **Active** company, otherwise 404. A slug is public information that names a tenant; it grants nothing.
- **Management routes** (`/api/v1/manage/c/{companyId}/...`): the identifier in the URL is a claim that is **verified on every request** against `CompanyMemberships` in the database. Only then is the tenant context set.
- **Platform routes** (`/api/v1/platform/...`): platform administrators only, limited to non-tenant data (companies and their status).
- A company identifier is never read from a request body, a query string, a custom header or the token. A missing context fails closed.
- The token carries **no company or company-role claims**. Membership is read from the database each request, so removing a staff member takes effect immediately, which a stateless 30-minute token could not do.

### 3. Roles

| Role | Where it lives | Scope |
|---|---|---|
| Platform Admin | global Identity role, created only by the configuration seed | companies and their status; **no automatic access to tenant business data** |
| Company Owner | `CompanyMemberships` row | one company: everything |
| Company Staff | `CompanyMemberships` row | one company: bookings, customer assistance, pickup and return only |
| Customer | a login linked to a customer record | their own reservations and rentals, per company |

Staff do **not** edit the fleet, see reports, change company settings or manage staff in the first release; those are owner-only. Every company always has at least one active owner. There is no public way to become a platform admin and no default accounts. See [data ownership and authorization](../multi-tenancy/data-ownership-and-authorization.md).

### 4. Customers

One global login can book with many companies. `Customers` stays the person (name). A new company-customer link carries the **per-company customer number**; reservations and rentals reference that link. A company sees only customers who booked with it and only name and number (not email) unless a later feature needs more. Walk-in customers created by staff have no login and belong to that company only. Customers are never merged across companies automatically.

### 5. Onboarding

A new company starts as **PendingReview** and is invisible to the public (its storefront returns 404) until a platform admin approves it. The owner may prepare it in the meantime. Statuses: PendingReview, Active, Suspended. A suspended company's storefront and staff operations are refused; its data is kept.

### 6. Storefronts first

Each company has its own storefront at `/c/{slug}`. A multi-company marketplace is deferred.

### 7. Time zones

Each company has a validated IANA time zone. "Today" for every date rule is the calendar date **in that company's zone**. Reservation and rental dates stay calendar dates (they do not shift with daylight saving); timestamps are stored in UTC. The container image must be proven to contain time zone data (this is an acceptance criterion, not an assumption).

### 8. Console application

The console client is preserved. Once tenancy is active it must be given an **explicit company** (and it connects with the same non-owner role as the API), so a console session can only ever act inside one named company. It is never a way to read across companies.

### 9. Migration: expand, backfill, verify, contract

Additive first, restrictive last, each step its own reviewed migration, with a `pg_dump` taken before the first one. Existing vehicles, reservations, rentals and customers move into a single default company with customer numbers unchanged. See [migration strategy](../multi-tenancy/migration-strategy.md).

### 10. Invitations

Staff are added by a single-use invitation link. The token is 256 random bits, only its SHA-256 hash is stored, it expires (72 hours), it is bound to an email address, and acceptance requires being signed in as that email. In the first release the owner copies the link; email delivery comes later. Tokens are never logged.

## What does not change

- The reservation rules, pricing, the vehicle row lock, the exclusion constraint and the one-active-rental index.
- The HttpOnly cookie session and its CSRF header.
- The password policy and lockout.

## Consequences

- More moving parts: a second database role, a tenant context, per-request membership lookups, and a larger test matrix. The isolation test suite is part of the feature, not an afterthought ([acceptance criteria](../multi-tenancy/acceptance-criteria.md)).
- A membership lookup per management request costs one indexed query. That price buys immediate revocation.
- Existing endpoints and the current website keep working through the migration by acting as the default company's storefront until the frontend moves to slug routes.
- After the contract step and once a second company exists, a schema rollback loses tenant data. That is the point of no return; the pre-migration backup is the recovery path.
- Revenue figures, when built, are "agreed rental value" taken from stored quotes. There is no payment concept in the system, so nothing may claim money was collected.
- Vehicle photographs and specifications are separate later features that need new tables, storage and API fields. They are not faked in the meantime.

## Spike outcome and production requirements

PR 14A-2 built an isolated proof of concept with 47 adversarial tests against a real PostgreSQL server (pooled connection reuse including with the driver's reset disabled, 300 concurrent requests from three companies over a pool of six, alternating tenants, exceptions, database errors, cancellation, missing and malformed tenants, role safety, cross-company reads, updates, deletes and aggregates). **No leak was found**, the harness is shown to detect leaks (negative controls and a mutation check in which disabling RLS made 18 of 25 tests fail), and the booking row lock and no-overlap exclusion constraint kept working under RLS. Full results, findings and limits: [spike README](../../tests/VehicleRental.TenantIsolation.Spike/README.md).

The spike code is **not** production code. The production implementation must meet these requirements:

1. **Transaction-local tenant setting.** The tenant is applied with `set_config('app.company_id', @id, true)` inside the transaction, by an EF Core transaction interceptor. A session-scoped setting is never used: it leaks when the driver does not reset the connection, and the driver's reset can hide that mistake.
2. **Explicit transactions for tenant-scoped reads.** Outside a transaction there is no setting and RLS returns nothing. Tenant code reads inside a transaction, and a guard refuses tenant commands outside one instead of silently returning empty results.
3. **Writes are transactional too.** EF Core sends a single-statement `SaveChanges` without a transaction, so tenant contexts use `AutoTransactionBehavior.Always` (or an equivalent approach proven by tests).
4. **A separate runtime database role** that is not the table owner, not a superuser and has no `BYPASSRLS`. Migrations run as a different, owning role. Today's Docker Compose uses one owner role for the API and CI's database user is a superuser; both change when tenancy is activated, and the application tests must run as the runtime role.
5. **Startup checks for unsafe roles.** The API refuses to start (or reports unhealthy) if its connection role is a superuser or has `BYPASSRLS`, if a tenant table lacks RLS, or if it owns a tenant table without `FORCE ROW LEVEL SECURITY`.
6. **Composite tenant-aware foreign keys.** Foreign-key checks ignore RLS, so a plain foreign key lets one company reference another company's row (proven). Every reference between tenant tables includes `CompanyId`.
7. **Company-scoped unique constraints.** A unique key across all companies leaks the existence of another company's data through duplicate-key errors (proven). `Vehicles.RegistrationNumber` and `Customers.CustomerNumber` become unique per company.
8. **Fail closed.** A missing, empty or malformed tenant returns no rows and refuses writes; a context with no company may not run any command.
9. **Integration with `UnitOfWork` and the booking guarantees.** The unit of work begins the tenant transaction first and `LockVehicleAsync` joins it; the vehicle row lock, the exclusion constraint and the one-active-rental index are unchanged, and the existing booking race tests are re-run as the runtime role.
10. **PgBouncer transaction pooling is not verified.** The setting is transaction-local, so it should work, but this was not tested and must not be assumed supported until it is.
11. **RLS does not protect against arbitrary SQL execution by a compromised application role.** That role can set the same setting. RLS here defends against bugs (a forgotten filter, a wrong query), not against an attacker who can already run arbitrary SQL as the application role. Compensating controls: parameterised EF queries only, the command guard, review of any raw SQL, and a role per company as the heavier upgrade path if the risk changes.

## Plan

| PR | Content |
|---|---|
| 14A-1 | This record and the supporting documents. No code. |
| 14A-2 | An isolated RLS and connection-pooling proof of concept with adversarial tests. No production table is touched. **Done** (47 tests; outcome above). |
| later (each needs approval) | schema expand, tenant context, backfill, repository scoping, contract migration, authorization, isolation test suite, slug routes in the website; then registration and invitations (14B) |
