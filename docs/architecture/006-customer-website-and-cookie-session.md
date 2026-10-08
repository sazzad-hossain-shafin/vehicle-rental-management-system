# ADR 006: Customer website and the HttpOnly cookie session

Status: accepted

## Context

The API issues a 30-minute JWT in the response body. A web client has to keep that token somewhere. Keeping it in JavaScript memory or `localStorage` means any script that runs on the page (a compromised dependency, an XSS bug) can read it. The API also had two gaps for a booking website: availability by date needed a sign-in, and there was no way to see the price before reserving.

## Decisions

**A browser session in an HttpOnly cookie.** `POST /auth/session` takes the same credentials as `/auth/login` and sets the same signed token as an `HttpOnly; SameSite=Strict; Path=/api` cookie that expires with the token. The body returns only the account and expiry. `DELETE /auth/session` clears the cookie. JavaScript cannot read the cookie, so the token is never stored, logged or exposed to the page. The JWT bearer handler takes the token from the cookie only when there is no `Authorization` header, so API clients and every existing test are unaffected, and the validation, claims and authorization are exactly the same ones. `Secure` is set whenever the request is HTTPS, or always when `Session:ForceSecureCookie=true` (needed behind a proxy that ends TLS).

**Same origin, no CORS.** The website and the API are served from one origin: an nginx container serves the built files and forwards `/api/` to the API (in development, the Vite dev server does the same). No CORS policy is added, so other origins cannot call the API from a browser at all.

**CSRF defence in depth.** Because browsers attach cookies automatically, a request authenticated by the cookie must also carry `X-Requested-With: VehicleRentalWeb` unless it is a safe method. Together with `SameSite=Strict`, no CORS and JSON-only bodies, a cross-site page cannot make such a request. Sign-in and sign-out require the header too. Requests with an `Authorization` header are not affected.

**Small read-only API additions.** `GET /vehicles/availability` becomes public, like vehicle browsing, because it reveals only which vehicles are free; booking still needs a sign-in. `GET /vehicles/{id}/quote` (public) returns the price from the existing pricing policy plus a snapshot of availability, so the page shows the authoritative price before the customer confirms without duplicating discount rules in the frontend. The promotional discount is not quoted, because customers cannot request it.

**The frontend holds no business rules.** Prices, availability, date limits, ownership and conflicts come from the API. The page validates only obvious mistakes (an empty date, a past pickup, a return that is not after the pickup) for instant feedback, and shows the API's own messages for everything else. A 409 is handled as an expected outcome, not an error screen.

**Plain React stack.** React, TypeScript (strict), Vite, React Router and TanStack Query for server state, with plain CSS design tokens and the native `<dialog>`, which keeps the dependency list short. API types are generated from the API's OpenAPI document.

## Consequences

- The session is still a stateless JWT: it cannot be refreshed or revoked. Sign-out removes the cookie from the browser; a copy of the token would stay valid until it expires (30 minutes). The website treats a 401 as an ended session and clears all cached customer data.
- Local Compose runs over plain HTTP, so the cookie is not `Secure` there. A deployment needs TLS and `Session__ForceSecureCookie=true` if TLS ends at a proxy.
- Making availability public lets anyone enumerate free vehicles for dates. That data is no more sensitive than the public vehicle list, but there is no rate limiting yet.
- The website adds a Compose service, a CI job and a Playwright job, and Dependabot keeps its npm dependencies current.
