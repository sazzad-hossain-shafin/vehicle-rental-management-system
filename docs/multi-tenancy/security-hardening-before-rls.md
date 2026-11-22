# Security hardening before Row-Level Security is switched on

Part of the [multi-company plan](README.md). Phase 14A-4 built the [tenant infrastructure](tenant-infrastructure.md) and the restricted runtime role, switched off. This page records what must be strengthened **before** the later steps rely on it, which step each item blocks, and how each one is proven.

Status words used below: **proven** (an automated test that runs in CI shows it), **documented** (written down, not enforced by a test), **planned** (not built).

## Summary

| # | Item | Severity today | Must be done before | Status |
|---|---|---|---|---|
| 1 | The SQL guard is a denylist, not a security boundary | medium | 14A-7 (stated clearly now) | documented |
| 2 | Raw ADO.NET and raw SQL bypass the EF interceptors | medium | **14A-5** (control), 14A-7 (complete) | planned |
| 3 | A compromised application role can set the tenant setting itself | medium (by design) | 14A-7 | documented |
| 4 | Defence in depth against SQL injection and arbitrary SQL | medium | **14A-5** (analyzers), 14A-7 (database limits) | partly proven |
| 5 | Runtime-role privilege boundary: `TEMPORARY`, `search_path`, timeouts | low to medium | 14A-7 | planned |
| 6 | PgBouncer compatibility | unverified | 14A-7, and before any deployment that uses PgBouncer | planned |
| 7 | `DatabaseRoleCheck` defaults to `Warn` outside Compose and CI | low | **14A-5** | planned |
| 8 | Console client compatibility and NULL-company writes | medium after the backfill | **14A-5** | planned |

The items marked **14A-5** are small and independent of the backfill itself. Each should be its own reviewed pull request, merged before the backfill pull request opens.

## 1. The SQL guard is not a security boundary

**What exists.** `TenantCommandGuard` refuses, for a tenant-bound context, any command outside the tenant transaction (**proven**), and any command whose text contains `app.company_id`, `set_config`, `set role`, `set session` or `row_security` (**proven** for those exact spellings).

**The limit.** The second check is a case-insensitive substring denylist. It is trivially bypassed by SQL that builds the text at run time, by extra whitespace (`SET  ROLE`), by `RESET ROLE`, or by a function that calls `set_config` internally. It exists to stop *mistakes* in reviewed code, not an attacker.

**Rule.** No document, review or test may describe the guard as an isolation control. Isolation is provided by the transaction-local setting plus (from 14A-7) RLS on real tables, under a role that cannot bypass it.

**Before 14A-7, acceptance criteria:**
- The ADR and the tenant-infrastructure page state that the guard is a bug-catcher, not a boundary. (Done in 14A-4 for the latter; repeat in the ADR.)
- A test documents a known bypass (for example `SELECT pg_catalog.set_config(...)` assembled with `format()`) and asserts that **RLS** still prevents cross-company access in that case, against the real tables with real policies. The guard must not be what the test relies on.

## 2. Raw ADO.NET and raw SQL bypass the interceptors

**What exists.** The EF Core interceptors see only commands EF Core sends. Anything executed through `Database.GetDbConnection().CreateCommand()`, a second `NpgsqlConnection`, or Dapper is invisible to them. Today, production code uses a raw command only in `DatabaseRoleSafetyCheck` (read-only catalog queries, no tenant), and raw SQL only in `UnitOfWork` (a constant `lock_timeout` and a parameterised row lock).

**Risk.** A future change opens a raw command in tenant code. It would run without the transaction check. Before 14A-7, nothing in the database would stop it from reading another company's rows.

**Control (before 14A-5).** An architecture test (an ordinary xUnit test, so it runs in CI) that scans the production assemblies and fails on:
- calls to `DatabaseFacade.GetDbConnection`, `new NpgsqlConnection`, `NpgsqlDataSource` or `DbConnection.CreateCommand` outside an explicit allowlist (`DatabaseRoleSafetyCheck`, the health check, the migration tooling);
- calls to `ExecuteSqlRaw`/`ExecuteSqlRawAsync`/`FromSqlRaw`/`SqlQueryRaw` outside an allowlist, where each allowlisted call site is named with a reason.

Also turn on the EF Core analyzer diagnostic for raw SQL built from interpolated strings (EF1002) as an **error**.

**Acceptance criteria:**
- Adding a `GetDbConnection().CreateCommand()` call to any non-allowlisted production class makes the build or the test suite fail. Proven by a test that runs the scanner against a deliberately bad sample type.
- The allowlist is a single, reviewed file. Changing it shows up clearly in a pull request.

**Before 14A-7:** RLS on real tables makes the database the boundary, so even an unnoticed raw command cannot read another company's rows. Proven by the test in item 1.

## 3. Tenant-setting spoofing by a compromised application role

**Fact.** Any session of the runtime role can run `set_config('app.company_id', <any id>, true)`. RLS policies that trust that setting therefore protect against **bugs** (a forgotten filter, a wrong query, a raw command), not against an attacker who can already run arbitrary SQL as the application. This is stated in the [ADR](../architecture/007-multi-company-tenancy.md) and on the [tenant-infrastructure page](tenant-infrastructure.md#limits). No test claims otherwise, and none should.

**Options, in increasing cost** (decision needed before 14A-7):
1. **Accept and compensate** (recommended for now): keep arbitrary SQL out of reach (items 2 and 4), keep the role minimal (item 5), and alert on unusual statements (`pg_stat_statements` review, a log alert on `set_config` from the runtime role outside the interceptor's exact statement).
2. **A signed tenant claim:** the setting carries the company ID plus an HMAC computed with a key the runtime role cannot read, verified by a `SECURITY DEFINER` function the policies call. This raises the bar but adds a key-management problem and a per-row function call. It must be benchmarked.
3. **One database role per company:** policies use `current_user`. This is the strongest option and the most operationally expensive (role per company, pool per role). Only for a future high-assurance tier.

**Acceptance criteria before 14A-7:** the decision is recorded in the ADR with the threat it does and does not cover. If option 1 is chosen, the log alert is implemented and tested on an isolated stack.

## 4. Defence in depth against SQL injection and arbitrary SQL

**What exists (proven).** All application queries are EF Core LINQ or parameterised `ExecuteSqlAsync` with interpolation handled by EF Core. The two `ExecuteSqlRawAsync` calls use a compile-time constant.

**Before 14A-5:** the architecture test and the EF1002 analyzer from item 2.

**Before 14A-7, as database limits on the runtime role** (each provisioned by `provision-runtime-role.sql` and asserted by `DatabaseRoleSafetyCheck`):
- `ALTER ROLE ... SET statement_timeout` (for example 30 s) and `idle_in_transaction_session_timeout` (for example 60 s), so a runaway or held transaction cannot pin a connection or a lock indefinitely. Check these against the booking lock timeout first.
- No `EXECUTE` on any `SECURITY DEFINER` function owned by the owner, unless it is reviewed and allowlisted in the role check.
- No extension that reaches outside the database (`dblink`, `postgres_fdw`, `file_fdw`, `adminpack`) installed in the application database. Asserted by the role check.

**Acceptance criteria:** RuntimeRoleTests assert each limit (the timeout values are set; `EXECUTE` on an owner-owned `SECURITY DEFINER` function is refused; a forbidden extension makes the check report unsafe).

## 5. Runtime-role privilege boundary

**Proven today:** not a superuser; no `CREATEDB`, `CREATEROLE`, `REPLICATION`, `BYPASSRLS`; `NOINHERIT`; member of no role; owns nothing; cannot create in the schema or the database; cannot truncate; cannot change the migration history; idempotent re-assertion; refuses the owner as the runtime role.

**Gaps found in review (planned, before 14A-7):**
- **`TEMPORARY` is not revoked.** PostgreSQL grants `TEMPORARY` on every database to `PUBLIC` by default. The runtime role can create temporary tables, and the schema search path consults `pg_temp` **first** for tables and views. A temporary table named like a real table shadows it for the rest of that session. Npgsql's default reset on returning a connection to the pool (`DISCARD ALL`) drops temporary tables, so the API is protected today. Under PgBouncer transaction pooling (item 6), or with `No Reset On Close`, a shadow table could survive into another request. Fix: `REVOKE TEMPORARY ON DATABASE <db> FROM PUBLIC` and grant it explicitly only to roles that need it (check the migration tooling first). Then assert in the role check that the runtime role has no `TEMPORARY`.
- **`search_path` is not pinned.** Set `ALTER ROLE <runtime> SET search_path = public` (with `pg_temp` placed explicitly last when temporary tables are needed by anyone), and make policies and any `SECURITY DEFINER` function schema-qualify every name.
- **The role check does not yet assert RLS.** 14A-7 adds: RLS **enabled and forced** on every tenant table, and the runtime role owns none of them. This part of the spike's check was deliberately left out of 14A-4.

**Acceptance criteria:** tests show that the runtime role cannot `CREATE TEMP TABLE`, that `SHOW search_path` for a fresh runtime session returns the pinned value, and that the role check reports unsafe for each of: `TEMPORARY` granted, RLS disabled on a tenant table, RLS not forced, a tenant table owned by the runtime role.

## 6. PgBouncer compatibility

**Status: unverified.** The tenant setting is transaction-local and applied as the first statement inside every tenant transaction, which is the pattern that *should* work under transaction pooling. Nothing has tested it.

**Before 14A-7, or before any deployment that puts PgBouncer in front of the API:** an isolated Compose test profile with PgBouncer in **transaction** mode between the API tests and PostgreSQL, running:
- the pooling suite (`TenantPoolingTests`) and the booking suite, with more client connections than server connections;
- a test that sets a session-level value and creates a temporary table on one client, and proves neither is visible to another client sharing the server connection. The temporary-table half must pass only after the `TEMPORARY` revoke in item 5;
- the Npgsql settings PgBouncer needs (`No Reset On Close=true` and prepared-statement handling) documented, with a test showing the transaction-local setting still disappears at commit and rollback without the driver reset.

**Acceptance criteria:** the profile runs in CI (or as a documented manual gate with its output attached to the 14A-7 pull request). Until then the docs keep the word "unverified".

## 7. Production-safe `DatabaseRoleCheck` default

**Today:** the code default is `Warn`. Compose and CI set `Enforce`. A deployment outside Compose that forgets the setting starts as an unsafe role (for example the owner) with only a warning.

**Change (before 14A-5):** the default depends on the environment. `Enforce` unless `ASPNETCORE_ENVIRONMENT` is `Development` (where it stays `Warn`). `Off` is allowed only in `Development` and `Testing`; elsewhere it is rejected at startup.

**Acceptance criteria:** API tests show that, with no explicit setting, Production starts in `Enforce` and refuses the owner role, Development starts in `Warn`, and `Off` in Production fails startup with a clear message. The runbook's emergency procedure (temporarily setting `Warn`) still works and remains documented as temporary.

## 8. Console client and NULL-company writes

**Today (proven by the existing suite, not run live):** the Console client is unchanged, binds no tenant, and connects with whatever connection string its user secrets hold, typically the owner. With no RLS and no query filter, it behaves exactly as before. It inserts vehicles, reservations and rentals with a NULL `CompanyId`.

**Problem after the backfill.** Every NULL row written after 14A-5 is a row 14A-6 and 14A-7 would hide or reject.

**Decision needed before 14A-5:**
- (a) the 14A-5 write-gap trigger fills a NULL `CompanyId` with the single company while exactly one exists, and raises otherwise. The Console needs no change for now. Or
- (b) the Console is given a configured company and connects as the runtime role. This is more work and also makes the Console subject to RLS later.

Recommended: **(a) for 14A-5, (b) before 14A-7**, because once there is more than one company, (a) correctly makes the Console's writes fail instead of guessing a company.

**Acceptance criteria for 14A-5:** an integration test runs the Console's own persistence path (the same services the Console uses) after the backfill, and proves its inserts receive the default company. A second test, with two companies, proves the same insert is **refused** with a clear message rather than assigned to a company. Before 14A-7: the Console runs as the runtime role with a configured company, and its suite passes under real RLS.

## What is not a gap

- **Rollback** of 14A-4 (previous compose file over the same volume, then forward again) was rehearsed on an isolated stack. It drops nothing.
- **Environment upgrade:** existing `.env` files need `POSTGRES_APP_PASSWORD`. `init-env --upgrade` adds only what is missing. Compose refuses to start with a clear message until it is done. This is an operator step, not a security gap, and it is in the [runbook](runtime-role-runbook.md).
