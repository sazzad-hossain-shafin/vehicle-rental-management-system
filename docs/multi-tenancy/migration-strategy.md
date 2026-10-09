# Migration strategy

Planned design; see [ADR 007](../architecture/007-multi-company-tenancy.md). Nothing here has been applied. Each step is a separate, reviewed pull request and migration.

## Principles

- **Expand, backfill, verify, contract.** Add first, restrict last.
- The application must keep working between steps. Until the contract step the previous release still runs correctly against the new schema, so rolling the application back is free.
- Existing customers, vehicles, reservations and rentals are preserved, with identifiers and customer numbers unchanged.
- The booking guarantees (exclusion constraint, one active rental per vehicle, the vehicle row lock, `xmin` versions) are never dropped or weakened at any step.
- A `pg_dump` is taken, and restore is rehearsed, before the first migration is applied anywhere that holds real data.

## Steps

### 1. Expand (additive only)

- Create `Companies`, `CompanyMemberships`, `CompanyCustomers`, `CompanyInvitations` and an audit table.
- Add a **nullable** `CompanyId` to `Vehicles`, `Reservations` and `Rentals`.
- Add a unique key `(CompanyId, Id)` on `Vehicles` and on the company-customer link, as targets for the composite foreign keys.
- Application code does not use any of it yet.

### 2. Backfill (one transaction, repeatable check)

- Create one default company. Its name, slug and time zone come from configuration supplied by the operator, never invented in code.
- Set `CompanyId` on every existing vehicle, reservation and rental.
- Create a company-customer link for every existing customer, keeping the current customer number.
- Convert every existing Staff user into a Staff membership of the default company. Existing Admin users remain platform admins.
- Status of the default company is Active (it is the existing business).

### 3. Verify (a query set, run and recorded)

- Row counts before and after match for every table.
- No `CompanyId` is NULL.
- For every reservation and rental, vehicle, customer link and the row itself have the same `CompanyId`.
- Every customer number is present in exactly one link of the default company.
- The existing test suite passes against the backfilled database.

### 4. Contract (separate migration)

- `CompanyId` becomes NOT NULL.
- The global unique indexes on registration number and customer number are replaced by per-company ones.
- Composite foreign keys are added (for example `Reservations(CompanyId, VehicleId)` to `Vehicles(CompanyId, Id)`).
- Row-Level Security policies are enabled and **forced**, and the application switches to a runtime role that does not own the tables.
- The old routes keep working as the default company's storefront until the website moves to slug routes.

### 5. Application cut-over

Repository and service scoping, the tenant context, membership-based authorization and the isolation test suite land in their own pull requests, each with the full existing test suite green.

## Rollback

| Point | Rollback |
|---|---|
| After step 1 | `Down` drops the new tables and columns. No data was changed. |
| After step 2 | Same, plus nothing to undo: the backfill only filled new columns and tables. |
| After step 4, with only the default company | A reverse migration restores the global unique indexes and drops the new constraints; no data is lost because only one company exists. |
| After step 4, with a second company | **Point of no return.** Collapsing two companies would break unique keys and lose the separation. Recovery is a restore from the pre-migration backup, accepting the loss of anything created since. |

The rollback and the restore are rehearsed on a copy of realistic data before the migration pull request is approved.

## Open technical checks (to be proven, not assumed)

- The time zone database is available in the runtime image (so the per-company IANA zone can be validated and used).
- The RLS approach survives connection pooling safely (the proof of concept in PR 14A-2).
- The migration job can create the non-owner runtime role and grant exactly the privileges the application needs, in Docker Compose and in CI.
