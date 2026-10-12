# The expand step (14A-3), the write gap, and the backfill and cutover strategy

Part of the [multi-company plan](README.md) ([ADR 007](../architecture/007-multi-company-tenancy.md)). The first section describes what Phase 14A-3 actually did. The rest is the **design** for the later steps and is not implemented.

## 1. What 14A-3 added

One migration, `AddCompanyTenancyExpand`. It is purely additive and nothing in the running application reads or writes any of it.

| Structure | Added |
|---|---|
| `Companies` | `Id`, `Slug` (3 to 63 characters, lower-case letters, digits and single hyphens; unique), `Name`, `Status` (`PendingReview`, `Active`, `Suspended`), `TimeZoneId`, `CreatedAt`, `ApprovedAt`, `xmin`. Checks keep the slug format, the known statuses, and "Active implies approved, PendingReview implies not approved". |
| `CompanyMemberships` | `Id`, `CompanyId`, `UserId`, `Role` (`Owner`, `Staff`), `Status` (`Active`, `Removed`), `CreatedAt`, `CreatedByUserId`. One row per company and user; removing keeps the row as history. |
| `CompanyCustomers` | `Id`, `CompanyId`, `CustomerId`, `CustomerNumber`, `CreatedAt`. The customer number is unique per company, and a person has one link per company. |
| `Vehicles` | nullable `CompanyId` with a foreign key to `Companies`, and a unique index `(CompanyId, Id)` (the target of later composite foreign keys). |
| `Reservations`, `Rentals` | nullable `CompanyId` with a foreign key to `Companies`, and a plain nullable `CompanyCustomerId`. |

`Customers` and `Users` are unchanged: a customer is a global person, linked to companies through `CompanyCustomers`. The existing `Admin` Identity role is kept as the future Platform Admin; it gains no access to tenant data.

On the existing aggregates the new columns are EF Core **shadow properties**, so the domain classes, the DTOs and the API contract are exactly as before (tests prove it, including that the OpenAPI document never mentions a company).

### Review of every index and foreign key in the expand phase

Each was kept only if it is useful now and cheap to add:

| Item | Decision | Reason |
|---|---|---|
| Foreign key `CompanyId` to `Companies` on the three existing tables | **kept** | Every value is NULL now, so validation is a trivial scan. It catches a wrong company ID during the backfill instead of leaving it for the contract step. |
| Index on `Reservations.CompanyId`, `Rentals.CompanyId` | **not created** | EF Core adds one for every foreign key by default. The column is always NULL and nothing queries by it, so it would only slow inserts. A customised convention skips exactly this key (an explicit allow-list of three keys, never a pattern); a test fails if the convention ever suppresses the index of any other foreign key in the model. The contract step adds the indexes its queries need. |
| Index on `CompanyMemberships.CreatedByUserId` | **not created** | Nothing looks members up by who added them. |
| `Vehicles (CompanyId, Id)` unique | **kept** | `Id` is already unique, so it can never reject an existing row; with `CompanyId` NULL it constrains nothing. It also serves as the foreign key's index. |
| `CompanyMemberships (CompanyId, UserId)` unique, `(UserId, Status)`, partial index on active owners | **kept** | The new tables are empty and each index matches a query management requests will make. |
| `CompanyCustomers` three unique keys and `(CustomerId)` | **kept** | They are the business rules (per-company number, one link per person) and the future composite-foreign-key target. |
| `CompanyCustomerId` foreign key (composite) | **deferred** | It needs the backfill first; added at the contract step. |
| NOT NULL, per-company unique keys, composite foreign keys, Row-Level Security, runtime role | **deferred** | Contract step. |
| `CompanyInvitations`, `AuditEvents` | **deferred to 14B** | Nothing in 14A needs them for security. |

### Locks and rewrites

- Adding a nullable column without a default does not rewrite a table (it is a catalog change), so row data and row versions are untouched. The rehearsal and the tests prove it: counts, every old column and every `xmin` are identical afterwards.
- Each `ALTER TABLE` takes a brief exclusive lock; adding a foreign key briefly blocks writes to both tables; building the unique index on `Vehicles` briefly blocks writes to that table. At this size that is milliseconds.
- The migration starts with `SET LOCAL lock_timeout = '5s'` inside its transaction: if a long booking transaction holds a conflicting lock, the migration **gives up after 5 seconds and leaves nothing half applied** instead of queueing and blocking every booking behind it. A test holds a lock and proves it.
- At a much larger scale the index would be built with `CREATE INDEX CONCURRENTLY` outside a transaction. That is not needed here.

### Rolling back

`Down` removes the new structures, but **refuses to run** if any company, membership, customer link or tenant column value exists (it raises an error and changes nothing), because dropping them would destroy tenant data. The way out is deliberate: restore the pre-migration backup, or remove that data on purpose first. Tests cover each of those conditions.

## 2. The write gap

Between this migration and the cutover, **the current application keeps creating vehicles, reservations and rentals with a NULL `CompanyId`** (and a NULL `CompanyCustomerId`). Nothing in 14A-3 closes that gap, and it is important not to pretend a one-time backfill does:

- Rows created after a backfill ran would be untagged again.
- The Console client and any older API instance behave the same way.
- Setting a database `DEFAULT` to "the first company" would be wrong the moment a second company exists: a booking for company B would silently be filed under company A. **No global default is used, ever.**

## 3. Backfill and cutover (design for the later steps)

Principles: the backfill is **repeatable and idempotent**, it never relies on running exactly once, and the gap is closed by the application writing the company explicitly, with a guard that fails closed in the meantime.

1. **Preconditions.** Exactly one company exists (the default company, created from operator configuration), and company registration is disabled. A second company cannot exist before the cutover completes.
2. **First backfill (14A-5).** Small batches, each its own short transaction with a lock timeout, ordered by `Id`:
   - `UPDATE "Vehicles" SET "CompanyId" = @default WHERE "CompanyId" IS NULL AND "Id" IN (next batch)`, and the same for reservations and rentals;
   - `INSERT INTO "CompanyCustomers" ... SELECT ... FROM "Customers" ON CONFLICT DO NOTHING`, keeping each customer's current number;
   - set `CompanyCustomerId` from the link;
   - convert existing Staff users into Staff memberships.
   Re-running does nothing for rows already done.
3. **Close the gap without a default.** Until the application sets the company itself, a `BEFORE INSERT` trigger on the three tables fills a NULL `CompanyId` **only while exactly one company exists**, and raises an error otherwise. It fails closed: with two companies a NULL insert is refused rather than guessed. It is dropped at the contract step.
4. **Application cutover (14A-6).** The application sets `CompanyId` and `CompanyCustomerId` explicitly on every insert. The Console client is updated to take an explicit company, or is not used until after the cutover. Old instances are drained.
5. **Final backfill and verification (run until clean).** Repeat step 2 until it reports zero rows, then verify and record the output:
   - no NULL `CompanyId` in the three tables, and no NULL `CompanyCustomerId` in reservations and rentals;
   - every reservation's and rental's vehicle and company link agree on the company;
   - every customer number appears exactly once in the default company's links;
   - row counts equal the pre-migration counts.
6. **Contract (14A-7), one short migration with a lock timeout.** To avoid long locks: add `CHECK ("CompanyId" IS NOT NULL) NOT VALID`, `VALIDATE` it, then `SET NOT NULL`; add the composite foreign keys `NOT VALID` then `VALIDATE`; replace the global unique keys with per-company ones; drop the trigger; add the tenant query indexes; enable Row-Level Security and switch to the runtime role.
7. **Only then** may a second company be created. Creating a company is refused while any tenant row still has a NULL `CompanyId`.

Rollback at each stage: before the contract step the previous application release still works against the expanded schema; the 14A-3 `Down` refuses once tenant data exists; after the contract step with a second company, recovery is the pre-migration backup (see the [rehearsal runbook](backup-restore-rehearsal.md)).

## 4. Limits of what is tested now

- The write gap is documented and the guard trigger is designed, not built or tested yet.
- Locking behaviour was verified on small tables only; the lock timeout is the protection for larger ones.
- The rehearsal copies the local demo database. A production-sized dataset has not been rehearsed.
