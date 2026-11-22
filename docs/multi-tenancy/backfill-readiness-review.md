# Backfill readiness review (before Phase 14A-5)

Part of the [multi-company plan](README.md). This is a **review, not an implementation**: it checks the backfill design in [expand and cutover, section 3](expand-and-cutover.md#3-backfill-and-cutover-design-for-the-later-steps) against the code as it is now, closes the gaps it finds, and sets the acceptance criteria the 14A-5 pull request must meet. Nothing here has been run against any database.

## Verdict

**Not ready to start yet.** The design is sound: an additive, idempotent, batched backfill under a fail-closed write guard. Four things must be settled first:
1. Merge the pre-14A-5 hardening pull requests: raw-SQL restrictions, the analyzers as errors, the role-check defaults, and the Console strategy.
2. Decide the open questions in the last section (the default company's identity, customer numbers, who runs the job).
3. Extend the design to the two **gaps found in this review**: Staff users created after the backfill, and links for customers who first book after it.
4. Agree the verification set and the rollback script below as part of the 14A-5 acceptance criteria.

## Starting point (from the code)

- **Expand step (14A-3, merged):**
  - nullable `CompanyId` on `Vehicles`, `Reservations` and `Rentals`;
  - nullable `CompanyCustomerId` on `Reservations` and `Rentals`;
  - the new tables `Companies`, `CompanyCustomers` and `CompanyMemberships`.
- **Companies:** a company needs a slug (3 to 63 characters, not reserved), a name, and an IANA time zone. It is created **awaiting review** and becomes Active only through `Approve`. Suspension and reactivation are separate transitions.
- **Staff users** are created by `AccountService` with the `Staff` role. Nothing gives them a company membership yet.
- **Legacy writers that leave `CompanyId` NULL:**
  - the API (tenancy off);
  - the Console client (see [its strategy](console-client-strategy.md); proven by characterisation tests);
  - an older API image during a rolling deployment;
  - any manual SQL.
- **Rehearsal tooling:** `db-backup.sh`, `db-restore.sh`, `db-fingerprint.sh` and `db-rehearse-migration.sh` (see [the rehearsal runbook](backup-restore-rehearsal.md)).

## 1. Operator-supplied default company

**Rule:** the default company's identity is **never invented in code**. The operator supplies it, and the job refuses to run without it.

- **Inputs** (environment of the one-shot job only, never in the repository):
  - `Backfill__DefaultCompany__Slug`
  - `Backfill__DefaultCompany__Name`
  - `Backfill__DefaultCompany__TimeZone` (an IANA name, for example `Australia/Melbourne`)
- **Validation** uses the domain's own rules (`Company` creation: slug format and reserved slugs, name length, a known IANA zone), so the backfill cannot accept a company the application would reject.
- **Create or verify:**
  - if no company exists, create it through the domain (`Create` then `Approve`) so it is Active;
  - if exactly one exists **with the same slug**, verify its name and time zone match and stop if they do not;
  - if a different company exists, or more than one, **stop** before touching any row.
- **Dry run** (`--dry-run`) prints the company it would create or verify, and the counts it would change, and changes nothing.

**Tests:** missing inputs refuse to run; each invalid input refuses with the domain's message; re-running with the same inputs is a no-op; different inputs against an existing company stop without changes; a second pre-existing company stops without changes.

## 2. Idempotent batch processing

**Who runs it:** a one-shot job connecting as the **table owner** (like `migrate` and `db-roles`), never the runtime role and never the API. It is idempotent by construction: every step selects only rows that still need it.

**Order** (each step completes before the next starts):
1. Default company (section 1).
2. **Write-gap guard installed** (section 3), so no new NULL rows appear while the backfill runs.
3. `CompanyCustomers`: one link per customer in the default company, **keeping the customer's current number**, with `ON CONFLICT DO NOTHING`.
4. `Vehicles.CompanyId` := default company.
5. `Reservations` and `Rentals`:
   - `CompanyId` := the **vehicle's** `CompanyId` (derived, never assumed);
   - `CompanyCustomerId` := the link for (that company, that customer).
6. `CompanyMemberships`: one Staff membership in the default company for every user in the `Staff` role. Admins are platform roles and get none.

**Batching:**
- Batches of a configurable size (default 1,000) by primary key: `WHERE "CompanyId" IS NULL ORDER BY "Id" LIMIT @n FOR UPDATE SKIP LOCKED`.
- Each batch is its own short transaction, with `lock_timeout` (for example 2 s) and `statement_timeout` (for example 60 s).
- Progress is logged per batch (table, batch number, rows changed). An interrupted run is safe to restart.
- The job ends by running the verification set (section 6) and exits non-zero if any check fails.

**Tests:**
- An empty database.
- A database with every kind of row.
- An interrupted run (cancel mid-batch) followed by a restart: same result as one clean run.
- Two concurrent runs: no duplicate links or memberships, and the same final state.
- A run while the API writes: new rows are covered by the guard; nothing left NULL.

## 3. Write-gap guard

A database trigger, because it covers **every** writer: the API, the Console, an old image, manual SQL.

- **`Vehicles`**, `BEFORE INSERT OR UPDATE OF "CompanyId"`: if `NEW."CompanyId"` is NULL, set it to the only company **only while exactly one company exists**; otherwise `RAISE` with a clear message. Setting a non-NULL `CompanyId` back to NULL is always refused.
- **`Reservations` and `Rentals`**, same events:
  - a NULL `CompanyId` is taken **from the row's vehicle**; if the vehicle has none, the Vehicles rule applies;
  - a NULL `CompanyCustomerId` is taken from the link for (that company, the row's customer). If the link does not exist, it is created with the customer's current number while exactly one company exists, and refused otherwise.

  This closes the second gap found in review: customers who register after the backfill and book later.
- **`CompanyMemberships`** (gap found in review): new Staff users need a membership. The trigger approach does not fit Identity's tables, so `AccountService` must add the Staff membership in the same transaction when it creates a Staff user, while exactly one company exists, and fail otherwise. This is the one application change 14A-5 needs. It must be tested on the original unit-of-work path with tenancy off.
- **Never a column default:** a default would silently keep assigning the default company after a second company exists.
- The functions are `SECURITY INVOKER`, schema-qualified, and set their own `search_path`. The runtime role already has the privileges they use.
- They are dropped at the contract step (14A-7), when `CompanyId` becomes `NOT NULL` and the application writes the company itself.

**Tests:**
- One company: inserts through the API, the Console path and raw SQL all get it; reservations and rentals get the vehicle's company and a link.
- Two companies: each of those inserts is refused, and nothing partial is committed.
- Setting `CompanyId` back to NULL is refused.
- A Staff user created after the backfill has a membership.
- Booking races still produce exactly one winner with the trigger in place.
- `ConsoleClientCompatibilityTests` is updated in the same pull request.

## 4. Console and other legacy writers

- **Console:** covered by the guard (option A of its [strategy](console-client-strategy.md)). Option B (a tenant-aware Console) is due before 14A-7, not in 14A-5.
- **Rolling deployment:** an older API image keeps writing NULLs, and the guard fills or refuses them. During the backfill window there must be exactly one company, which is guaranteed because company registration does not exist yet. The job checks again before every batch.
- **Seeding:** the admin seeder and the Console's sample data go through the same tables, so the guard covers them.

## 5. Backup and restore rehearsal

Before 14A-5 touches any real data:
1. `bash scripts/db-backup.sh`: a private dump outside Git (the `backups/` folder is ignored).
2. `bash scripts/db-restore.sh --file <dump>`: restore into a **scratch** database. Never the development database without explicit approval.
3. Fingerprint before (`db-fingerprint.sh`). Run the backfill against the scratch copy. Run the verification set. Fingerprint after.
4. Record in the pull request: duration, rows changed per table, every verification result, and the rollback run (section 6) on a second scratch copy.
5. Drop the scratch databases (the script asks first).

The rehearsal so far used the local demo database. A production-sized dataset has not been rehearsed. That is a stated limit of 14A-5, not something to claim.

## 6. Verification queries and rollback

**Verification set** (run by the job at the end, and stored with the pull request; each must return 0 unless noted):
- rows with NULL `CompanyId` in `Vehicles`, `Reservations` and `Rentals`;
- rows with NULL `CompanyCustomerId` in `Reservations` and `Rentals`;
- reservations and rentals whose `CompanyId` differs from their vehicle's;
- reservations and rentals whose link belongs to another company or another customer;
- customers with a reservation or rental but no link in that company;
- customer numbers appearing more than once within a company, and links whose number differs from the customer's;
- Staff users without an active membership in the default company;
- the number of companies (must be exactly 1);
- **row counts** of `Vehicles`, `Reservations`, `Rentals` and `Customers` equal before and after (compared with the fingerprint).

**Rollback** (the backfill is additive: it fills NULL columns and adds rows, and changes and drops nothing else):
- A reviewed `rollback-backfill.sql`, run as the owner in one transaction:
  1. drop the guard triggers;
  2. set the filled columns back to NULL;
  3. delete the links and memberships the job created (recorded in a `BackfillLog` table with the batch ID);
  4. delete the default company only if nothing else references it.
- It is **rehearsed on a scratch copy** and proven to restore the "before" fingerprint exactly.
- The pre-backfill backup remains the last resort. Nothing in the rollback drops a table, a database or a volume.
- There is **no point of no return** in 14A-5. That begins at the contract step (14A-7) once a second company exists.

## 7. Protection against incorrect company assignment

- **Single-company precondition**, checked at start and before every batch. Two companies means stop, never "pick one".
- **Derive, never assume:** reservations and rentals take the company of their vehicle; links take the company of their row.
- **No heuristics:** a row whose parent is missing or inconsistent (for example a rental whose vehicle has another company) **stops the job** and is reported. It is never "fixed" automatically.
- **Recorded:** every change is attributable to a batch in `BackfillLog` (table, key, batch, timestamp). That makes rollback exact and an audit possible.
- **Verified:** the job is not "done" until every verification query passes. A failure exits non-zero, and the stack's start-up waits for it, as it does for `migrate` and `db-roles`.

## Open decisions (needed before the 14A-5 pull request)

1. **The default company's identity:** slug, name and time zone for the real deployment, and who supplies them.
2. **Customer numbers:** keep the global number as the company number for existing customers (recommended, matching the design), and confirm the numbering for new customers after the cutover.
3. **Who runs the job and where:** a Compose one-shot `backfill` service, started only by an explicit profile (recommended, so a normal `docker compose up` never runs it), or a manual command.
4. **The Staff membership role** to grant existing Staff users (recommended: `Staff`, not `Owner`), and who becomes the default company's `Owner`.
5. **Whether a `BackfillLog` table** (needed for an exact rollback) is acceptable as a migration in 14A-5.

## Acceptance criteria for the 14A-5 pull request

- Sections 1 to 3 and 6 to 7 are implemented with the tests listed. The Staff-membership change in `AccountService` is tested on the tenancy-off path.
- The rehearsal (section 5) is run on a scratch copy, and its output is attached: counts, durations, verification results, rollback proof.
- No tenancy, query filter or RLS is enabled. The API still runs with `Tenancy:Enabled=false`.
- All five CI checks pass, including the runtime-role run, and the architecture tests (no new raw SQL outside the allowlist, or the allowlist change is reviewed).
- The development database is untouched unless its owner approves the run separately.
