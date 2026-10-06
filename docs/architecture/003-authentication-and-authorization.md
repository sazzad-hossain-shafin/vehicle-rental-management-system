# ADR 003: Authentication, authorization and customer ownership

Status: accepted

## Context

After the HTTP API and persistence existed, every endpoint was open. The API needed to know who is calling, limit what each kind of caller may do, and stop one customer from reading another customer's data.

## Decisions

**ASP.NET Core Identity for accounts, JWT bearer tokens for sign-in.** Identity owns password hashing, validation and lockout, so none of that is written by hand. A successful login returns a short-lived (30 minute) HMAC-SHA256 token carrying only the account ID, roles and, for customers, the customer ID. There are no refresh tokens yet, which keeps the first version small; the cost is that a token cannot be revoked before it expires.

**Identity lives in Infrastructure, apart from the domain `Customer`.** The login and the business record are linked by one nullable, unique `Users.CustomerId` column. The Domain and Application layers do not reference Identity. Staff and admins have no customer record, and desk-created customers need no login.

**Roles map to named policies.** Endpoints require a policy (`FleetManage`, `CustomerManage`, `RentalManage`, `UserAdministration`, `CustomerSelfService`), not roles. Several policies currently share the same roles so they can diverge later without touching the endpoints.

**Default deny.** A fallback policy requires sign-in for everything not explicitly marked anonymous. A test enumerates every mapped endpoint against the documented access matrix and fails if one is unaccounted for.

**Ownership comes from the token.** A customer's ID is read only from the signed token, never from a URL, query or body. `/me/...` endpoints take no ID, and `GET /rentals/{id}` filters by the caller's customer ID in the database query. Someone else's rental answers 404, identical to a missing one, so identifiers cannot be probed.

**Secrets are configuration.** The signing key is supplied through user secrets or environment variables, and the API refuses to start with a missing or weak key.

## Consequences

- Sign-in requires a PostgreSQL database, because accounts are stored there with the business data in one transaction.
- Tokens remain valid for up to 30 minutes after a role change or lockout.
- A symmetric signing key means every service that verifies tokens holds the secret; a multi-service system would move to asymmetric keys.
- Refresh tokens, MFA, email verification, password reset and rate limiting are not implemented. See [authentication](../authentication.md) for the access matrix and limitations.
