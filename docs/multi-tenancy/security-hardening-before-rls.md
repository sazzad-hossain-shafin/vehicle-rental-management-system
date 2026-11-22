# Security hardening before Row-Level Security is switched on

Part of the [multi-company plan](README.md). Phase 14A-4 built the [tenant infrastructure](tenant-infrastructure.md) and the restricted runtime role, switched off. This page records what must be strengthened **before** later steps rely on it, which step each item blocks, and how each one is proven.

Status words: **proven** (an automated test in CI shows it), **in review** (implemented in an open pull request), **documented** (written down, not enforced), **planned** (not built), **decision needed**.

## Summary

| # | Item | Blocks | Status |
|---|---|---|---|
| 1 | The SQL guard is a denylist, not a security boundary | 14A-7 | documented |
| 2 | Raw ADO.NET and raw SQL restricted to reviewed call sites | **14A-5** | in review |
| 3 | EF1002 and EF1003 are errors in every build | **14A-5** | in review |
| 4 | `DatabaseRoleCheck` is `Enforce` by default outside Development; `Off` refused there | **14A-5** | in review |
| 5 | Console client strategy and NULL-company writes | **14A-5** (decision), 14A-7 (implementation) | in review (strategy) |
| 6 | Tenant-setting spoofing by a compromised application role | 14A-7 | **decision needed** (alternatives evaluated below) |
| 7 | `TEMPORARY` revoked; `search_path` pinned and validated | 14A-7 | planned |
| 8 | Statement, idle-in-transaction and transaction timeouts | 14A-7 | planned |
| 9 | Real-table RLS: enabled, **forced**, complete, and verified by the role check | 14A-7 | planned |
| 10 | Tests that deliberately bypass the EF Core interceptors | 14A-7 | planned |
| 11 | Runtime-role least privilege, views and functions, and production-safe migration credentials | 14A-7 (and any production deployment) | planned |
| 12 | PgBouncer transaction-pooling compatibility | 14A-7, and any deployment that uses PgBouncer | planned (unverified) |

Everything marked **14A-5** is small, independent of the backfill itself, and has its own pull request. The backfill pull request should not open until they are merged.

## 1. The SQL guard is not a security boundary

`TenantCommandGuard` refuses, for a tenant-bound context, any command outside the tenant transaction (**proven**). It also refuses command text containing `app.company_id`, `set_config`, `set role`, `set session` or `row_security`, as a case-insensitive substring check (**proven** for those spellings).

The second check is a denylist. It is bypassed by text built at run time, extra whitespace (`SET  ROLE`), `RESET ROLE`, or a function that calls `set_config` internally. It exists to catch **mistakes** in reviewed code. No document, review or test may describe it as an isolation control.

**Before 14A-7:** the ADR says so explicitly, and the bypass tests in item 10 prove that RLS, not the guard, stops cross-company access when the guard is evaded.

## 2. Raw ADO.NET and raw SQL (pre-14A-5, in review)

The EF Core interceptors see only commands EF Core sends. A `DbCommand` from `GetDbConnection()`, a new `NpgsqlConnection`, or a Raw SQL method is invisible to them.

**Control:** architecture tests (`tests/VehicleRental.ArchitectureTests`) scan every production source file:
- raw connections, commands and Raw SQL methods are allowed only at reviewed call sites with an **exact count** and a written reason;
- SQL text must be a compile-time constant, including when it arrives through a variable, `string.Format` or a helper (a private helper is checked at each call; a wider one is never trusted);
- the parameterising APIs (`ExecuteSql`, `FromSql`, `SqlQuery`) must be given an interpolated string literal.

**Acceptance (met in the pull request):** self-tests for every pattern, and a deliberate violation in production code fails the build with file and line.

**Limit:** this is a syntax scan. Reflection, `dynamic`, a `using` alias that renames an Npgsql type, or a target-typed `new()` not assigned to a typed variable are not seen. The database (item 9) is the boundary.

## 3. EF1002 and EF1003 as errors (pre-14A-5, in review)

Measured with EF Core 10.0.12:
- **EF1002** flags interpolation passed straight to a Raw method.
- **EF1003** flags non-constant concatenation passed straight to a Raw method.
- Neither flags a local variable or `string.Format`; item 2 covers those.

By default they are warnings, so a local or Docker build succeeded. `src/Directory.Build.props` makes them errors in every build. Architecture tests check the setting, that no project hides it, and that nothing suppresses either rule.

## 4. `DatabaseRoleCheck` defaults (pre-14A-5, in review)

When the setting is absent:
- Development uses `Warn`;
- **every other environment uses `Enforce`**;
- `Off` is refused outside Development.

An explicit `Warn` outside Development remains the runbook's emergency setting, and is logged as temporary.

**Acceptance (met in the pull request):** policy unit tests for every combination; API tests proving that Production with no setting refuses the owner, starts with the runtime role, and refuses `Off`.

## 5. The Console client (pre-14A-5 decision, in review)

The strategy, the options and the acceptance criteria are in [the Console client strategy](console-client-strategy.md). Characterisation tests pin down today's behaviour: NULL company on every write, even with companies present.

## 6. Tenant-setting spoofing: alternatives and their guarantees

**The fact.** The transaction-local setting `app.company_id` is a custom setting, and PostgreSQL lets **any** role set a custom setting. Policies that read it therefore trust whoever can run SQL on the application's connection. Alerts and logs can only detect misuse, not prevent it. **This is not accepted as a residual risk without a decision.** The alternatives below are evaluated so the decision can be made before 14A-7.

**Threats considered:**

| | Threat |
|---|---|
| T1 | an application bug: a forgotten filter, a wrong query, a raw command in the wrong place |
| T2 | SQL injection in one request: the attacker controls SQL text inside a transaction that is already bound to *their own* company |
| T3 | arbitrary code execution in the API process: the attacker has everything the process has (memory, configuration, connections) |
| T4 | stolen runtime-role database credentials, used from outside the application |

| Option | T1 | T2 | T3 | T4 | Cost |
|---|---|---|---|---|---|
| **O1. Setting + policies** (current design) | **yes** | **no**: injected SQL can call `set_config` for another company | no | **no**: the thief sets any company | lowest |
| **O2. Signed tenant claim, verified by the database.** The app gets a short-lived token per transaction (company, transaction ID, expiry, HMAC). A `SECURITY DEFINER` function owned by a separate verifier role checks the HMAC with a key the runtime role cannot read, and records the company for the current transaction in a table the runtime role cannot write. Policies compare `CompanyId` with `(SELECT app.current_company())`. | yes | **yes**: injected SQL cannot mint a token for another company, because the key is not reachable through SQL | **no**: the process holds the key or the signer | **yes**: credentials alone cannot read any company | moderate: key management and rotation, one verification per transaction (the wrapped sub-select is evaluated once per statement, not per row), a migration for the verifier role and functions |
| **O3. Login role per company** with a connection pool per company | yes | **yes**: injected SQL cannot change the login role | no: the process holds every credential | partial: one stolen credential exposes one company | high: a role and a pool per company, connection count grows with companies, provisioning and rotation per company |
| **O4. Schema or database per company** | yes | yes | no | partial | highest: migrations and reporting per company |
| **O5. Compensating controls only** (no arbitrary SQL by construction, items 2 to 4; least privilege; `pgaudit` on `set_config` and role changes; alerts) | yes | reduces the likelihood of SQL injection, does not contain it | no | no | low, needed in every option anyway |

**No option protects against T3.** An attacker inside the process acts as the application. That is contained by process hardening, least-privilege credentials, monitoring and incident response, not by the database.

**Recommendation:** run a **spike for O2** before 14A-7, with O5 in every case. O2 is the only option that adds protection against both SQL injection and stolen credentials at moderate cost. O3 and O4 are recorded as heavier upgrade paths.

**Acceptance criteria for the decision** (recorded in ADR 007 before 14A-7):
- The spike proves, on the real tables:
  - a raw runtime-role connection that calls `set_config('app.company_id', <B>)` reads and writes **nothing** of company B;
  - a simulated injection inside a company A transaction cannot read company B;
  - a token for A cannot be replayed in another transaction, or after it expires;
  - key rotation works without downtime.
- **Performance budget:** the booking and availability suites stay within 10% of their p95 latency under O2.
- If O2 is rejected, the ADR states plainly that T2 and T4 are not prevented, and names who accepted that and why.

## 7. `TEMPORARY` and `search_path`

**Gap found in review.** PostgreSQL grants `TEMPORARY` on every database to `PUBLIC` by default, so the runtime role can create temporary tables. For tables and views, `pg_temp` is searched **first** unless it is placed explicitly. A temporary table named like a real table therefore shadows it for the rest of that session. EF Core's generated SQL uses unqualified table names, so it would read the shadow.

Npgsql's reset on returning a connection to the pool (`DISCARD ALL`) drops temporary objects, so the API is protected **today**. Under PgBouncer transaction pooling, or with `No Reset On Close=true`, a shadow table could survive into another request.

**Changes:**
- `REVOKE TEMPORARY ON DATABASE <db> FROM PUBLIC`, then grant it only to roles that are shown to need it. The migration bundle and the owner are checked first. A superuser is unaffected.
- `ALTER ROLE <runtime> IN DATABASE <db> SET search_path = pg_catalog, public, pg_temp`. `pg_temp` is listed last, so it can never shadow anything.
- Every policy and every `SECURITY DEFINER` function schema-qualifies its names and sets its own `search_path`.

**Acceptance criteria and tests:**
- The runtime role cannot `CREATE TEMP TABLE` (error `42501`).
- A fresh runtime-role session reports the pinned `search_path`, through a raw connection and through EF Core.
- The role check reports unsafe when the runtime role has `TEMPORARY`, or when its `search_path` differs from the pinned value.
- With `TEMPORARY` granted back temporarily in a test, a temporary table called `Vehicles` is still **not** used by an unqualified query, because `pg_temp` comes last.

## 8. Timeouts

Today only the booking path sets `SET LOCAL lock_timeout = '5s'`. A runaway or deliberately slow statement, or a transaction left open, can hold a connection and its locks indefinitely.

**Changes** (role-level defaults set by the provisioning script; the values are reviewed against the booking suite first):
- `statement_timeout` (proposed 30 s), so no single statement runs unbounded.
- `idle_in_transaction_session_timeout` (proposed 60 s), so an abandoned transaction releases its locks and its connection.
- `transaction_timeout` (PostgreSQL 17; proposed 120 s), so a whole transaction cannot run unbounded.
- `lock_timeout` stays per transaction where the code chooses it.

**Acceptance criteria and tests:**
- The values are visible in `pg_db_role_setting` for the runtime role, and the role check reports their absence.
- A statement beyond the limit is cancelled with SQLSTATE `57014`. The connection returns to the pool and serves the next request with no tenant left behind.
- An idle transaction is terminated, and the API turns that into its normal error, not a hang.
- The whole booking, concurrency and pooling suite passes unchanged with the timeouts in place.

## 9. Real-table RLS, forced and verified

**Requirements for 14A-7:**
- **Every** table with a `CompanyId` column has RLS **enabled and forced** (`relrowsecurity` and `relforcerowsecurity`). Forcing makes the owner subject to the policies too. Superusers and roles with `BYPASSRLS` always bypass RLS, which is why the API's role may be neither.
- Each such table has policies for `SELECT`, `INSERT`, `UPDATE` and `DELETE`, with both `USING` and `WITH CHECK` where they apply. The predicate fails closed: a missing or empty setting matches no row and permits no write. There is no permissive `true` policy.
- The list of tenant tables is derived from the catalog (columns named `CompanyId`), not maintained by hand.

**Acceptance criteria and tests:**
- The role check (startup and `/health`) reports unsafe when any tenant table lacks enabled or forced RLS, or lacks a policy for any command. Each case is a separate test.
- A test migration that adds a new table with `CompanyId` but no RLS makes the check fail.
- Every policy predicate is tested with: no setting, an empty string, a malformed UUID, the nil UUID, company A, company B.

## 10. Tests that deliberately bypass the EF Core interceptors

These prove that the database, not the application code, enforces isolation. They run as the runtime role on the real tables with real policies:
- A raw `NpgsqlConnection` with **no** tenant setting: every tenant table reads zero rows; inserts and updates are rejected.
- Raw `set_config('app.company_id', A, true)`: only A's rows are visible. Updating or deleting B's row by ID affects zero rows. Inserting a row for B is rejected by `WITH CHECK`.
- A session-level `set_config(..., false)` followed by `COMMIT`, then a new transaction on the same physical connection: no leak.
- The denylisted statements built at run time (`format()`, `RESET ROLE`, a function calling `set_config`) evade the SQL guard, and RLS still holds.
- `COPY` as the runtime role cannot read or load another company's rows.
- **Under O2** (item 6), the same spoofing attempts read and write nothing.

## 11. Least privilege, views, functions and migration credentials

**Runtime role** (proven today: not a superuser; no `CREATEDB`, `CREATEROLE`, `REPLICATION` or `BYPASSRLS`; `NOINHERIT`; no memberships; owns nothing; no `CREATE` on the schema or the database; no `TRUNCATE`; cannot change the migration history). Planned additions:
- **Views:** any view over a tenant table must be `security_invoker = true` (PostgreSQL 15 and later). Otherwise it runs with its owner's rights and bypasses RLS. The role check lists views owned by privileged roles and reports any without it.
- **Functions:** the runtime role may execute no `SECURITY DEFINER` function except those on a reviewed list (for example O2's verifier). The role check reports any other.
- **Extensions:** no extension that reaches outside the database (`dblink`, `postgres_fdw`, `file_fdw`, `adminpack`) in the application database. The role check reports them.
- **Database-level:** `REVOKE CONNECT ON DATABASE <db> FROM PUBLIC`, then grant it to the owner and the runtime role only.

**Migration credentials (production):**
- The table owner is a **dedicated non-superuser role** that owns the schema and tables. It has no `BYPASSRLS`, so forced RLS binds it as well. The cluster superuser is used only to bootstrap that role, never by a job that runs regularly.
- Separate secrets: the owner's password exists only in the migration job and the `db-roles` job, never in the API's environment. The API's password exists only in the API.
- Rotation for each secret is documented and rehearsed. Compose keeps its simpler local setup (the bootstrap user is the owner), labelled as development only.

**Acceptance criteria and tests:** each role-check addition has a test that creates the unsafe object and sees it reported. A production deployment checklist in the runbook covers the owner role and the separation of secrets.

## 12. PgBouncer transaction pooling (unverified)

The tenant setting is transaction-local and applied as the first statement inside each tenant transaction, so transaction pooling *should* work. **Nothing tests it yet**, and the docs keep saying "unverified" until something does.

**Test profile** (isolated Compose, PgBouncer in `pool_mode = transaction` between the tests and PostgreSQL, fewer server connections than clients):
- the pooling, booking and concurrency suites pass;
- a session-level setting, a session advisory lock or a temporary table made by one client is never visible to another client sharing the server connection. The temporary-table part must pass only after item 7;
- Npgsql runs with `No Reset On Close=true` (PgBouncer resets nothing in transaction mode), and prepared statements are handled as PgBouncer requires (`max_prepared_statements`, or Npgsql's automatic preparation disabled). Both are documented with the versions tested;
- the transaction-local setting is gone after commit, rollback and a server-side error, without any driver reset.

**Acceptance:** the profile runs in CI, or as a documented manual gate with its output attached to the 14A-7 pull request.

## What is not a gap

- **Rollback** of 14A-4 (previous compose file over the same volume, then forward again) was rehearsed on an isolated stack. It drops nothing.
- **Environment upgrade:** existing `.env` files need `POSTGRES_APP_PASSWORD`. `init-env --upgrade` adds only what is missing, and Compose refuses to start with a clear message until it is done. It is in the [runbook](runtime-role-runbook.md).
