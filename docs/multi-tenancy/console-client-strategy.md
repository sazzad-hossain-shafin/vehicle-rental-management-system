# The Console client in a multi-company system

Part of the [multi-company plan](README.md). This page decides how the Console client keeps working, and stays safe, as tenancy arrives. It is a **strategy for review**. Nothing here changes the Console yet.

The Console client in this repository is the portfolio copy. The original university submission lives elsewhere, is protected, and is never modified by this work.

## What the Console does today

- **Menu:** list, search and filter vehicles; rent a vehicle (which can create a customer); return a rental; show rental history. On first run it seeds sample vehicles.
- **Connection:** its own connection string from user secrets, a git-ignored local file or an environment variable. In local development that is usually the table owner.
- **Persistence path:** `PersistenceSession` constructs a `VehicleRentalDbContext` directly. There is no dependency injection, no `TenantContext` and no tenant interceptors. The same application services the API uses (`VehicleService`, `RentalService`) run on the original unit-of-work path.

**Proven by `ConsoleClientCompatibilityTests`** (this pull request), through that exact path, connecting as the restricted runtime role, on a database without Row-Level Security:

| Behaviour today | Consequence |
|---|---|
| Writes vehicles and rentals with `CompanyId` NULL and `CompanyCustomerId` NULL, **even when companies exist** | every Console write after the backfill (14A-5) is a row with no company |
| Creates customers globally, with no `CompanyCustomers` link | the customer is invisible to every company once links are required |
| Reads every vehicle, whichever company it belongs to | today correct (one business); after 14A-7 a tenant-less read must find nothing |
| Works as the restricted runtime role | the Console does not need the owner's privileges |

## What each later phase would do to it, unchanged

| Phase | Effect on an unchanged Console |
|---|---|
| 14A-5 backfill | its new rows have no company. **Must be handled in 14A-5** |
| 14A-6 query filters (API) | none: the Console does not go through the API, and it would keep seeing every company |
| 14A-7 forced RLS | as the runtime role without a tenant: reads nothing and writes are rejected (the Console breaks, safely). As the **owner or a superuser**: RLS does not bind it (a superuser always bypasses it; an owner bypasses it unless RLS is forced), so it would read and write **every company's data**. That must be impossible |

## Options considered

| | Option | Guarantees | Costs and limits |
|---|---|---|---|
| A | **Database write-gap guard** (part of 14A-5): a `BEFORE INSERT OR UPDATE` trigger fills a NULL `CompanyId` with the only company while exactly one exists, and **raises** otherwise | covers every legacy writer at once (the Console, an older API image during a rollout, any script), because it lives in the database. Never guesses: with two companies the write fails | does not make the Console tenant-aware. The Console still reads every company until 14A-7, and stops working for writes as soon as a second company exists |
| B | **Tenant-aware Console:** the Console is given one company by operator configuration, composes the same services as the API through dependency injection, and runs every command inside the tenant transaction (`ITenantScopeRunner`) as the restricted runtime role, with the database role check enforced at startup | the same isolation path as the API: transaction-local tenant setting, command guard, and (from 14A-7) RLS. A Console configured for company A cannot read or write company B | real work: replace `PersistenceSession` with the DI composition, add configuration and startup validation, and decide customer numbers per company |
| C | **Retire the Console's writes** (read-only, or archive it) in this repository | smallest attack surface | loses the demonstration of the original OOP client working against the real database |

## Recommendation

1. **14A-5:** option **A** as the safety net, for every writer, not only the Console. No Console change.
2. **Before 14A-7:** option **B**. The Console must run as the restricted runtime role with a configured company, or not at all.
3. Option **C** remains the fallback if B turns out disproportionate. It needs its own decision.
4. **Never** run the Console as the table owner or a superuser once 14A-7 is in place. Option B enforces this with the same startup role check as the API.

## Company assignment rules for option B

- The company comes **only from operator configuration** (a company slug, for example `Console:CompanySlug`), never from menu input, a file the user picks, or the database "first row".
- At startup the Console resolves the slug through `ICompanyDirectory` to an **Active** company and refuses to start if it is missing, ambiguous, suspended or pending. The message names the slug and the problem, never a connection string.
- Every menu command is one unit of work in its own tenant transaction (`ITenantScopeRunner.RunAsync`). Nothing is cached across commands, as today.
- Customer numbers become per company through `CompanyCustomers` (the 14A-5 decision on customer numbers applies).
- Background or bulk work (the sample-data seed) runs for that one company only. There is no "all companies" mode.

## Acceptance criteria and tests

**14A-5** (with the backfill pull request):
- With exactly one company, a write through the Console's path gets that company. `ConsoleClientCompatibilityTests` is updated deliberately to assert it.
- With two companies, the same write is **refused** with a clear message, and nothing partial is committed.
- The backfill rehearsal includes rows written through the Console path after the backfill started, and the verification queries find none left without a company.

**Before 14A-7** (option B):
- The Console starts only with a valid Active company slug and a role that passes the database role check (`Enforce`).
- Under real, forced RLS, its whole menu flow passes. A Console configured for company A finds none of company B's vehicles, rentals or customers, and cannot write to them.
- Connecting it as the owner or a superuser makes it refuse to start.
