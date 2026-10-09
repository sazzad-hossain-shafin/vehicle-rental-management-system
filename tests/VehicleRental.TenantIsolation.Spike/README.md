# Row-Level Security and connection-pooling proof of concept

Phase 14, PR 14A-2. Supports the multi-company decision in ADR 007 (added in PR 14A-1). This is a **spike**: it answers one question, whether PostgreSQL Row-Level Security (RLS) driven by a transaction-local setting can isolate companies safely with EF Core and Npgsql connection pooling. It is not production code.

- It references **no production project** and touches **no production table**. It creates its own database, three roles and a few tables, and drops them afterwards.
- Nothing in the running application, its schema, its configuration or its tests changes.
- It is part of the solution, so CI runs it with the other database tests.

## Result

**The approach is safe against the failure modes it was asked to cover, with the conditions and limits listed below.** 47 adversarial tests pass (the full solution: 807 tests pass, none skipped). No leak was found across pooled connections, and the tests include negative controls that prove the harness would see a leak if there were one.

**The tests can fail.** As a mutation check, RLS was temporarily disabled on `vehicles` and `reservations` in the schema: 18 of the 25 tests in the cross-company, pooling and fail-closed groups failed (the 7 that passed do not depend on those two tables, such as the negative controls). The schema was then restored.

## How to run it

It needs a PostgreSQL server where your login may create databases **and roles** (a throwaway container is the easiest):

```bash
docker run -d --name rls-spike -e POSTGRES_PASSWORD=<choose-a-password> -p 127.0.0.1:55432:5432 postgres:17
export VEHICLERENTAL_TEST_CONNECTION="Host=localhost;Port=55432;Username=postgres;Password=<the-password>;GSS Encryption Mode=Disable"
dotnet test tests/VehicleRental.TenantIsolation.Spike
docker rm -f rls-spike
```

Without the variable the tests are skipped (and CI fails a skipped test). Each run creates uniquely named databases and roles with random passwords and removes them at the end.

## The design under test

1. Every tenant table has `company_id`; RLS is **enabled and forced** (the table owner is filtered too).
2. Policies compare `company_id` with `app_company_id()`, which reads the setting `app.company_id` and returns NULL for anything that is not a well-formed UUID. `company_id = NULL` matches nothing, so a missing, empty or malformed tenant **fails closed**.
3. The setting is applied with `set_config('app.company_id', @id, true)`. The `true` makes it **transaction-local**: PostgreSQL discards it at COMMIT or ROLLBACK, on every path. It is applied by an EF Core `DbTransactionInterceptor` (`TransactionStarted`) for any transaction opened on a tenant-bound context, so there is nothing to reset and nothing to forget.
4. A scoped `TenantSession` runs one unit of work in one transaction for one company.
5. The application connects as a role that **owns nothing and has no BYPASSRLS**. A startup check (`RlsSafetyCheck`) detects superusers, `BYPASSRLS` roles and un-forced owned tables.
6. A command guard (a `DbCommandInterceptor`) refuses tenant-bound commands that run outside a transaction, refuses a context with no company, and refuses raw SQL that touches the tenant setting. It catches mistakes; it is not the security boundary (RLS is).
7. Composite foreign keys `(company_id, vehicle_id) -> vehicles(company_id, id)` and per-company unique keys keep rows and references inside one company.
8. The only cross-company read is a narrow `SECURITY DEFINER` function (`platform_list_companies`) returning id, slug and status.

## What was tested

| Required scenario | Tests |
|---|---|
| Pooled connection reuse | pool of one physical connection serves two companies in turn (same backend PID, no leak); isolation holds even with Npgsql's connection reset **switched off** |
| Concurrent requests from different companies | 300 overlapping requests, three companies, pool of 6: every request reads only its own rows (also with `IgnoreQueryFilters()`), and each company ends with exactly its own writes |
| Sequential alternating tenants | 100 alternations on a small pool |
| Exceptions and rollbacks | handler exception, database error (unique violation), cancelled request (`pg_sleep` cancelled): work rolled back, connection reusable and tenant-free |
| Missing or invalid tenant | no setting, empty, malformed, nil UUID, unknown UUID, SQL-injection text: no rows readable, writes refused (`42501`) |
| Queries outside explicit transactions | refused by the guard (not silently empty); EF's single-statement `SaveChanges` is made tenant-scoped with `AutoTransactionBehavior.Always` |
| Background and administrative operations | a job visits each company in its own tenant transaction and sees only that company; the platform function exposes only slug and status and cannot be hijacked with a temporary table |
| Roles without BYPASSRLS or ownership | the application role passes the safety check and cannot disable RLS, drop or create policies, create tables, `SET ROLE`, or use `row_security = off`; a `BYPASSRLS` role, a superuser and an un-forced owned table are all detected, and the bypass role really does see every company |
| EF interceptor and transaction lifecycle | tenant set at transaction start (explicit and implicit), cleared at commit |
| Cross-company read, update, delete, aggregate | reads by ID, updates and deletes (0 rows), inserting or moving a row into another company (`42501`), `count`, `sum`, `GROUP BY`, joins, `EXISTS` subqueries, and `SELECT ... FOR UPDATE` (nothing to lock) |

Also covered because they decide whether RLS is enough on its own:

- the production **vehicle row lock** (`SELECT ... FOR UPDATE`, ADR 005) still serialises bookings of one vehicle inside a tenant transaction;
- the production **no-overlap exclusion constraint** still works under RLS, treats adjacent days as allowed, and ignores other companies' vehicles;
- the policy condition **can use the company index** (checked with `EXPLAIN`).

## Findings

1. **Transaction-local settings work with EF Core and Npgsql.** Applying `set_config(..., true)` from `TransactionStarted` is reliable, including with the driver's reset turned off. The design does **not** rely on Npgsql resetting pooled connections.
2. **A session-scoped setting is unsafe, and the driver can hide it.** The negative control leaks company A's rows to the next caller when the reset is off; with the default reset the same mistake is masked. Never use a session-scoped tenant setting.
3. **EF Core does not open a transaction for a single-statement `SaveChanges`.** Without one there is no tenant setting and RLS hides the row. Fix: `Database.AutoTransactionBehavior = Always` on tenant contexts. **Reads need an explicit transaction**, and the guard refuses anything else instead of returning an empty result.
4. **The application must not connect as the table owner, a superuser or a `BYPASSRLS` role.** Today's Docker Compose uses one owner role for the API, and CI's database user is a superuser; the real migration must introduce a separate runtime role and run the application tests as that role. `RlsSafetyCheck` should run at API startup.
5. **Foreign-key checks ignore RLS.** A plain foreign key lets company B reference company A's row (proven). Composite `(company_id, id)` foreign keys refuse it. These are required, not optional.
6. **A unique index across all companies leaks existence.** Company B inserting A's plate receives a duplicate-key error (proven). Production has exactly this today (`Vehicles.RegistrationNumber`, `Customers.CustomerNumber`); both must become per-company.
7. **The booking guarantees survive.** The row lock and exclusion constraint behave as before under RLS, and a lock on another company's vehicle locks nothing.
8. **A controlled escape hatch exists without BYPASSRLS.** A `SECURITY DEFINER` function with a pinned `search_path` and schema-qualified names returned only slug and status and resisted a temporary-table hijack. `companies` is deliberately not forced so such a function can read it.
9. **Cost.** On this machine a tenant transaction plus an EF read took about 5 ms per unit of work against about 2 ms for a bare query (indicative only, not like for like: the first includes opening a transaction and the EF pipeline). The policy condition is usable as an index condition.

## Limits, stated plainly

- **RLS keyed on a setting protects against bugs, not against an attacker who already runs arbitrary SQL as the application role.** That role can set the same setting (`KNOWN_LIMITATION_...` test). Compensating controls: parameterised EF queries only, the command guard, code review of any raw SQL, and no SQL injection surface. A stronger design (a database role per company with `SET LOCAL ROLE`) was considered and rejected for now as heavier than the risk warrants; it remains the upgrade path.
- **PgBouncer transaction pooling** should work because the setting is transaction-local, but it was **not** tested here.
- **Metadata:** planner statistics and `pg_class` row estimates are not tenant-filtered, so approximate table sizes are visible to anyone who can query the catalog. Error messages were checked for the cases above only.
- **Long transactions:** the design needs a transaction per unit of work. Wrapping a whole HTTP request in one holds a connection for its duration; the real implementation should keep units of work short and bounded (a lock timeout already exists).
- **Integration is not done here.** The existing `UnitOfWork` opens its own transaction lazily for the vehicle lock; the production change must begin the tenant transaction first and have `LockVehicleAsync` join it.

## Recommendation

**Proceed with RLS as the backstop** (ADR 007 decision 1), under these conditions for the production migration:

1. A runtime role that owns nothing and has no `BYPASSRLS`; RLS forced on every tenant table; `RlsSafetyCheck` at startup and in the integration tests (which must run as that role, not as a superuser).
2. Transaction-local tenant setting applied by an interceptor; tenant contexts use `AutoTransactionBehavior.Always`; reads inside an explicit transaction; the command guard stays.
3. Composite tenant-aware foreign keys and per-company unique keys in the same migration that enables RLS.
4. `LockVehicleAsync` joins the tenant transaction; the existing booking race tests are re-run as the runtime role.
5. The limits above are recorded in the threat model.

## Files

| File | Purpose |
|---|---|
| `schema.sql` | throwaway tables, function, policies, privileges (embedded; the future migration can borrow the shapes) |
| `Tenancy/` | the design under test: context, interceptors, tenant session, safety check |
| `Support/` | fixture that creates and drops the database and roles |
| `*Tests.cs` | the adversarial tests, grouped by concern |
