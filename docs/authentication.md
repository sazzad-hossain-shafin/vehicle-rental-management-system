# Authentication and authorization

How the API knows who you are and what you may do. Framework details are in the ASP.NET Core documentation; this page covers the choices made here.

## How it works

```
POST /api/v1/auth/login  {email, password}
        │
        ▼
ASP.NET Core Identity checks the password hash and lockout
        │
        ▼
200  { accessToken: "<JWT>", tokenType: "Bearer", expiresAtUtc: "...", user: {...} }

GET /api/v1/anything-protected
Authorization: Bearer <JWT>
```

- **Identity** (in the Infrastructure project) owns users, roles, password hashing and validation, and lockout. Nothing here hashes or compares passwords by hand.
- **JWT bearer tokens** carry the proof of sign-in. A token is accepted only if its signature, issuer, audience and expiry are valid, and only for the one algorithm used (HMAC-SHA256). Unsigned tokens and other algorithms are refused.
- **Tokens are short-lived (30 minutes by default).** There are no refresh tokens yet, so sign in again when a token expires.
- A token holds only: the account ID (`sub`), a token ID, the role(s), and for customer accounts the customer ID (`customer_id`), plus issuer, audience and times. No email, no name, no password data.
- Every protected request is checked in the API. Anything not explicitly marked anonymous requires a sign-in, so a new endpoint cannot be left open by accident. (This also means unknown paths answer 401 to anonymous callers.)

## Roles

| Role | Who | Can do |
|------|-----|--------|
| **Admin** | The business owner | Everything Staff can, plus create staff accounts |
| **Staff** | Rental-desk employees | Add vehicles, register and look up customers, start and return rentals, read the whole rental history |
| **Customer** | An online customer | Read their own profile and their own rentals. Nothing else |

Role names live in one place (`Roles` in the Application project). Endpoints name a **policy** (a capability), not roles:

| Policy | Roles |
|--------|-------|
| `FleetManage` | Staff, Admin |
| `CustomerManage` | Staff, Admin |
| `RentalManage` | Staff, Admin |
| `UserAdministration` | Admin |
| `CustomerSelfService` | Customer (and the token must carry a customer ID) |

Several policies currently have the same roles; they are separate so they can diverge later (for example, a read-only staff role) without touching the endpoints.

## Authorization matrix

| Endpoint | Access |
|----------|--------|
| `GET /health`, `GET /health/live` | Anonymous |
| `POST /auth/login`, `POST /auth/register` | Anonymous |
| `GET /vehicles`, `GET /vehicles/{id}`, `GET /vehicles/by-registration/{n}` | Anonymous (browsing the fleet is public) |
| `POST /vehicles` | Staff, Admin |
| `POST /customers`, `GET /customers/{id}`, `GET /customers/by-number/{n}` | Staff, Admin |
| `POST /rentals`, `POST /rentals/{id}/return`, `GET /rentals` | Staff, Admin |
| `GET /rentals/{id}` | Signed in. Staff/Admin: any rental. Customer: only their own, otherwise 404 |
| `POST /admin/staff` | Admin only |
| `GET /me` | Any signed-in account |
| `GET /me/customer`, `GET /me/rentals` | Customer accounts only |
| `/openapi/v1.json`, `/scalar/v1` | Anonymous, Development only |

A test enumerates every mapped endpoint and fails if one is neither on this list nor deliberately anonymous.

Customers cannot start or return rentals: that is a desk operation until a proper booking flow exists.

## Customer ownership

Identity (the login) and the customer (the business record) are separate things, linked by one nullable column:

- `Users.CustomerId` points at a `Customers` row. It is set for customer accounts and empty for staff and admins.
- It is unique where set, so each customer has at most one login.
- Deleting a customer that has a login is refused by the database.
- Customers created at the desk, or that existed before accounts, simply have no login. Nothing requires one.
- The domain classes know nothing about Identity; the link lives entirely in the Infrastructure project.

How a customer is kept to their own data:

1. The customer ID comes **only from the signed token**, never from a URL, query string, header or body. `/me/customer` and `/me/rentals` take no ID at all.
2. `GET /rentals/{id}` for a customer filters by their customer ID in the database query. Someone else's rental is answered **404**, identical to a rental that does not exist, so IDs cannot be probed.
3. Staff-only endpoints answer 403 to customers, including for the customer's own ID (they use `/me/...`).
4. The customer-scoped history query counts and pages only that customer's rows.

## Accounts

- **Customer registration** (`POST /auth/register`, public): creates the customer record, the login and the Customer role in **one database transaction**; if anything fails, nothing is kept. Customer numbers are generated (`WEB-...`). The request has no role field, and any extra role or customer data in the body is ignored.
- **Staff** accounts are created by an Admin with `POST /admin/staff`; the role is fixed to Staff.
- **Admin** accounts cannot be created through the API. The first admin is created at startup from configuration (below).
- The three roles are created by the database migration itself, so they exist in every database.

### Password and sign-in rules

- Passwords: at least 10 characters, with an upper-case letter, a lower-case letter and a digit. They are stored as salted PBKDF2 hashes by Identity.
- Login name is the email, matched case-insensitively. Emails are unique.
- **Lockout:** 5 failed attempts lock the account for 15 minutes.
- **Login failures all look the same** (wrong password, unknown email, locked account): the same 401 and message, and an unknown email still performs a password-hash check, so response time does not reveal which accounts exist.

## Configuration and secrets

| Setting | Purpose | Where it comes from |
|---------|---------|---------------------|
| `Jwt:SigningKey` | Signs and verifies tokens. **A secret** | User secrets or environment (`Jwt__SigningKey`). **Never committed** |
| `Jwt:Issuer`, `Jwt:Audience` | Who issued the token and who it is for | `appsettings.json` (not secret) |
| `Jwt:AccessTokenMinutes` | Token lifetime (1-120, default 30) | `appsettings.json` |
| `Seed:Admin:Email`, `Seed:Admin:Password` | Optional initial admin. **A secret** | User secrets or environment. Never committed |

**The API refuses to start** if the signing key is missing or weak (under 32 characters, or with little variety), or the issuer or audience is missing, in every environment. It never generates a key: a generated key would silently invalidate every token on restart and hide the misconfiguration. Error messages never contain the key.

### Development setup

```bash
# a random signing key, stored in user secrets (not in the repository)
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/VehicleRental.Api
# PowerShell:  [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))

# optional: create an initial admin at startup (the password must satisfy the password rules)
dotnet user-secrets set "Seed:Admin:Email" "admin@example.test" --project src/VehicleRental.Api
dotnet user-secrets set "Seed:Admin:Password" "<a strong password>" --project src/VehicleRental.Api
```

The admin is created once, only when the database is migrated. An existing account is never modified or given a new password, and the password is never logged. If the credentials are missing, no admin is created (and the log says so).

For anything beyond local development, supply the same settings through environment variables or your platform's secret store.

## Using it

1. `POST /api/v1/auth/login` with email and password, and copy `accessToken`.
2. Send `Authorization: Bearer <accessToken>` on later requests.
3. In the interactive documentation (`/scalar/v1`, Development only), use the **Authorize** option and paste the token. Protected operations are marked in the document.

## Errors

Authentication and authorization failures are Problem Details like every other error: **401** when you are not signed in (missing, malformed, expired or wrongly signed token, with a `WWW-Authenticate: Bearer` header) and **403** when you are signed in but your role is not allowed. Responses never explain *why* a token was rejected, never name the roles that would have been allowed, and never contain internal details.

## Limitations and future work

- **No refresh tokens or revocation.** A token stays valid until it expires (30 minutes), even if the account is locked or its role changes in the meantime. Deleting an account does stop `/me`.
- **No email verification, password reset or change-password flow**, and no multi-factor authentication or social login.
- **Registration reveals whether an email is taken** (a 409), as most sign-up forms do. Login does not.
- **No rate limiting** beyond account lockout. Lockout can be used to lock a victim's account out for 15 minutes.
- **Customer self-service is read-only.** Booking, and customers starting their own rentals, belong to a later phase.
- The signing key is symmetric (HMAC), so every service that verifies tokens holds the secret. A multi-service setup would move to asymmetric keys.
