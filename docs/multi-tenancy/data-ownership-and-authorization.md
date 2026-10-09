# Data ownership and authorization

Planned design; see [ADR 007](../architecture/007-multi-company-tenancy.md).

## Data ownership

| Data | Owner | Notes |
|---|---|---|
| Company (name, slug, status, time zone, settings) | the platform (rows are readable by that company's members) | `Status`: PendingReview, Active, Suspended |
| Company membership (user, company, role) | the company; managed by its owners | one row per user and company; roles Owner or Staff |
| Company invitation | the company | stores a token **hash**, never the token |
| Vehicle | exactly one company | registration number unique **per company** |
| Company-customer link (company, customer, customer number) | the company | customer number unique per company |
| Customer (the person: name) | the person, via their login | shared across companies only through links the customer created by booking |
| Reservation, rental | exactly one company, and one customer link | composite foreign keys guarantee vehicle, customer link and company agree |
| User login (email, password hash) | the platform | one login can book with many companies and belong to many companies as staff |
| Audit events | the platform | membership changes, invitations, status changes, any future support access |

Rules that follow from this:

1. Every tenant-owned row carries a `CompanyId`; there are no tenant rows without one.
2. A row never references a row of another company. This is enforced by composite foreign keys, not only by code.
3. A company sees a customer only through its own company-customer link: name and customer number. Email addresses are not shown to companies in the first release.
4. Deleting history is refused as today (restrictive foreign keys).

## Roles and capabilities

| Capability | Platform Admin | Owner | Staff | Customer |
|---|---|---|---|---|
| Approve, suspend or reactivate a company | yes | | | |
| See the list of companies and their status | yes | own company | own company | |
| Read tenant business data (vehicles, reservations, rentals, customers) | **no** (explicit, time-limited, audited support access only; not built) | yes | yes | own only |
| Manage staff and invitations | | yes | | |
| Company settings (name, slug, time zone) | | yes | | |
| Create or edit vehicles | | yes | | |
| Reports | | yes | | |
| Create bookings for a customer, assist customers | | yes | yes | |
| Pickup and return | | yes | yes | |
| Cancel any reservation of the company | | yes | yes | |
| Browse vehicles, get a quote | public (active companies only) | | | |
| Reserve, view and cancel **own** reservations | | | | yes |

Invariants:

- A company always has at least one active owner; removing or demoting the last one is refused.
- A staff member cannot invite, promote or remove anyone.
- Nobody becomes a platform admin through the API. Platform admins are created only by the configuration seed (as the existing admin is today), and the seed is never given a default password.

## Tenant resolution

| Route family | Company comes from | Verification |
|---|---|---|
| `/api/v1/c/{slug}/...` public and customer | the slug | company must exist and be **Active**; otherwise 404 |
| `/api/v1/manage/c/{companyId}/...` | the URL | an active membership for the signed-in user is required, looked up in the database on every request |
| `/api/v1/platform/...` | none | platform admin role; only non-tenant data |

The company is never taken from a body, query string, custom header or token. The token holds no company or company-role claims, so a membership change applies immediately. The resolved company is stored in a scoped tenant context that sets the EF query filter and the PostgreSQL transaction-local setting.

## Invitations

1. An owner creates an invitation for an email address and a role (Staff in the first release).
2. The server generates 256 random bits, stores only their SHA-256 hash with the company, the normalised email, the role, the creator and an expiry (72 hours), and shows the link to the owner once.
3. The invitee opens the link, signs in (or registers) and accepts. Acceptance requires that the signed-in account's email equals the invited email, that the invitation is unexpired, unrevoked and unused, and it is consumed in the same transaction that creates the membership.
4. Attempts are rate-limited; tokens never appear in logs; revoking an invitation is immediate.

Because email delivery does not exist yet, the owner passes the link on. Email confirmation of accounts is a later step; until then acceptance relies on the email-match rule and the secrecy of the token.

## Customers across companies

- One login, one customer record. Booking with a company creates (or reuses) that company's link and customer number for the customer.
- "My reservations" lists the customer's own reservations across companies, labelled by company, because a customer is entitled to their own data everywhere.
- A staff member of company A cannot look up a customer by number unless that customer has a link with company A.
