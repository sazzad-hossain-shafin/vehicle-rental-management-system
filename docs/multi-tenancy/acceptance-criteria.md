# Acceptance criteria

Planned design; see [ADR 007](../architecture/007-multi-company-tenancy.md). A step may merge only when its criteria are met and CI is green. "Existing tests" means the full current backend, frontend and end-to-end suites.

## PR 14A-1: record the architecture

- [x] The ADR states the approved decisions, the alternatives rejected and the consequences.
- [x] Data ownership, authorization boundaries, tenant resolution, migration strategy, threat model and acceptance criteria are written down.
- [x] No source, schema, configuration or runtime behaviour changes.
- [x] The documentation index links the new documents; CI is green.

## PR 14A-2: RLS and connection-pooling proof of concept (done)

The proof of concept lives in its own test project, uses its own throwaway tables and schema, and touches no production table or runtime path.

- [x] Reproducible: one command (and CI) creates the schema, roles and data from scratch.
- [x] The runtime role owns nothing and has no `BYPASSRLS`; the tables have RLS enabled and forced.
- [x] The tenant setting is transaction-local, set inside the transaction that runs the queries.
- [x] Adversarial tests pass, each demonstrating isolation rather than assuming it:
  - pooled connection reuse, including a pool of size one
  - sequential requests alternating between two companies
  - many concurrent requests from different companies
  - an exception, a rollback and a cancelled request followed by reuse of the connection
  - missing, empty and invalid tenant context fail closed (no rows, no writes)
  - queries run outside an explicit transaction
  - a background or administrative operation with an explicit, narrow scope
  - cross-company read, insert, update, delete and aggregate attempts
  - a role that owns the table or has `BYPASSRLS` is detected as unsafe
- [x] Results, the design chosen, and any limitation are documented. If RLS cannot be made safe in this code base, the work stops with a written recommendation for a safer design.
- [x] Existing tests are unchanged and green.

## PR 14A-3: schema expand

- [ ] One generated migration, `AddCompanyTenancyExpand`, creates `Companies`, `CompanyMemberships` and `CompanyCustomers` and adds nullable `CompanyId` (and `CompanyCustomerId` on reservations and rentals); it contains no statement that changes an existing row.
- [ ] The new columns on existing tables are EF shadow properties: no domain class, DTO, endpoint or OpenAPI change.
- [ ] Every existing test passes unchanged, apart from the one assertion that counts a rental's foreign keys (it now expects the new optional company key).
- [ ] A test seeds data in every existing table, applies the migration, and shows identical counts, old-column data and `xmin` row versions.
- [ ] Up, Down and Up again work on a disposable database; Down restores the exact previous schema and data; Down refuses once any tenant data exists.
- [ ] The migration gives up after its lock timeout instead of waiting behind a long transaction, and leaves nothing half applied.
- [ ] Tests cover slug validation (domain and database), reserved names, collisions, membership uniqueness and history, per-company customer numbers, customer links, foreign keys, status constraints and the reviewed index list.
- [ ] The vehicle row lock, the no-overlap exclusion constraint and the one-active-rental index are unchanged.
- [ ] A backup, restore and migration rehearsal on a copy of the development data passes, the live development database is not migrated, and the result is recorded.
- [ ] The write gap and the backfill and cutover strategy are documented. No backfill, tenant resolution, RLS, runtime role, authorization, registration, invitation or frontend change is included.

## Later steps (for reference, each needs approval)

**Schema expand and backfill**
- [ ] The application behaves exactly as before; existing tests pass.
- [ ] The verification query set is run and its output attached to the pull request.
- [ ] `Down` migrations work and are rehearsed on a copy of realistic data.

**Contract and RLS activation**
- [ ] Composite foreign keys reject a cross-company reference in a database-level test.
- [ ] The application runs as the non-owner role; booking race tests still pass, including two companies at once.

**Authorization**
- [ ] Every endpoint has a row in a two-company authorization matrix test, including foreign IDs, paging and counts.
- [ ] Staff cannot perform any owner-only action; the last owner cannot be removed; removed staff are refused at once.

**Time zones**
- [ ] The runtime image contains time zone data and an invalid zone is rejected at company creation.
- [ ] Booking, cancellation and pickup date rules are tested around midnight and daylight saving transitions for at least two zones.

**Frontend**
- [ ] Storefront slug routes work; a pending or suspended company shows a clear not-found page.
- [ ] Route guards are convenience only: every protected action is also refused by the API.
