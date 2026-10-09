# Threat model: isolation between companies

Planned design; see [ADR 007](../architecture/007-multi-company-tenancy.md). Each row names the control that is meant to stop the threat and the test that must prove it. A control without its test does not count.

## Assets and actors

- **Assets:** a company's fleet, reservations, rentals, customer list, staff list and settings; a customer's personal data and bookings; platform administration.
- **Actors:** anonymous visitor, customer, staff of company A, owner of company A, owner of company B, platform admin, someone who steals a token or an invitation link, a buggy deploy.
- **Trust boundary:** the browser is untrusted for everything, including the company it says it is acting for.

## Threats and controls

| # | Threat | Control | Proving test |
|---|---|---|---|
| T1 | Guessing or enumerating another company's vehicle, reservation, rental or customer IDs | query filters, RLS and composite foreign keys; a foreign ID answers **404**, the same as a missing one | for every endpoint, owner of A requests IDs of B |
| T2 | Editing the company ID in the URL of a management route | membership verified in the database on every request | valid user, foreign company ID, expect refusal |
| T3 | Supplying a company or role in a body, header, query string or token | those inputs are never read for tenant decisions; the token carries no company claims | forged claim or extra field is ignored |
| T4 | Paging, sorting, filtering or counting reveals other tenants' rows or totals | the filter applies before paging; `totalCount` is computed in scope | two companies with different row counts, compare totals and pages |
| T5 | Reports or aggregates mix tenants | same scope as any query; no cross-tenant aggregate is exposed | report totals equal the sum of one company's own rows |
| T6 | A background job, seed or admin operation touches the wrong tenant, or none | jobs set an explicit company or use a clearly named system path; nothing runs with an implicit "all" scope | job tests; code review checklist for each new job |
| T7 | The tenant setting leaks to the next request through a pooled connection | transaction-local setting (cannot outlive its transaction); proven by the proof of concept | pooled reuse, alternating and concurrent tests ([PR 14A-2](../architecture/007-multi-company-tenancy.md#plan)) |
| T8 | The application connects as the table owner or with `BYPASSRLS`, silently skipping RLS | a runtime role that owns nothing and has no `BYPASSRLS`; RLS forced on the tables; a test that fails if the role could bypass | role attribute check, cross-company read as the runtime role returns zero rows |
| T9 | A missing or malformed tenant context returns data or errors open | fail closed: no setting means no rows and no writes | requests with no context, empty or invalid value |
| T10 | Two companies' bookings interfere (locks, constraints) | locks and constraints are per vehicle and a vehicle has exactly one company | the existing race tests plus a two-company run |
| T11 | A removed staff member keeps access | membership read from the database each request, not from the token | remove membership, the very next request is refused |
| T12 | Staff escalate (invite, promote, change settings, edit fleet) | capability policies; staff hold operations only; the last owner cannot be removed | staff attempts each owner-only action |
| T13 | Invitation theft, replay or use by the wrong person | 256-bit token, hash stored, single use, 72 hour expiry, bound to the invited email, rate limited | replay, expiry, wrong account, brute force attempts |
| T14 | Someone becomes platform admin | no endpoint creates one; the seed needs explicit configuration and has no default | registration and invitation can never yield that role |
| T15 | A platform admin reads tenant data without authorisation | no automatic access; any future support access must be explicit, time-limited and audited | platform admin requests tenant data, expect refusal |
| T16 | A pending or suspended company is reachable by the public | storefront and public APIs return 404 unless the company is Active | status matrix test |
| T17 | Customer data leaks between companies | a company sees only its own links, name and customer number; emails are not exposed | staff of A looks up a customer who only booked with B |
| T18 | Slug squatting or enumeration of companies | reserved words, uniqueness, rate limits; unknown, pending and suspended all answer the same 404 | status and unknown slugs are indistinguishable |
| T19 | Cross-site request forgery against management actions | the existing SameSite cookie and `X-Requested-With` header rule applies to every unsafe method | existing tests extended to new routes |
| T20 | Sensitive data in logs | logs carry the company ID and a hashed user ID, never emails, tokens or passwords; invitation tokens are never logged | log capture test on invitation flows |
| T21 | Wrong date rules for a company in another time zone | "today" is computed in the company's validated IANA zone | boundary tests across midnight and daylight saving changes |
| T22 | A migration corrupts or loses data | expand, backfill, verify, contract; backup and restore rehearsed | verification query set and rollback rehearsal |

## Residual risks accepted for now

- No email confirmation exists, so invitation acceptance relies on the email-match rule and token secrecy.
- A platform operator with database credentials can read everything; this is an operational risk handled by access control and backups, not by the application.
- Tokens are stateless for 30 minutes; user disablement is not instant. Membership changes are instant because membership is not in the token.
