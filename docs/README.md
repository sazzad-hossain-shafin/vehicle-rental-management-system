# Documentation

Start with the [project README](../README.md) for an overview and the Docker quick start.

## Using and running

- [Running with Docker](docker.md): the Compose environment, configuration, migrations, health checks and troubleshooting.
- [Local development without Docker](development.md): running the API, console client and tests with the .NET SDK and your own PostgreSQL.
- [API guide](api.md): routes, conventions, paging and error responses.
- [Reservations](reservations.md): booking, date semantics, availability, cancellation, pickup and the concurrency guarantees.
- [Authentication and authorization](authentication.md): sign-in, roles, the access matrix, customer ownership and secrets.

## Architecture

- [Architecture overview](architecture/overview.md): layers, dependency direction, the running system and CI at a glance.
- Decision records:
  - [ADR 001: PostgreSQL persistence](architecture/001-postgresql-persistence.md)
  - [ADR 002: Docker development environment](architecture/002-docker-development-environment.md)
  - [ADR 003: Authentication, authorization and ownership](architecture/003-authentication-and-authorization.md)
  - [ADR 004: Reservations, availability and pickup](architecture/004-reservations.md)

## Quality and delivery

- [Continuous integration](ci.md): what the GitHub Actions workflow checks, its policies, and how to run each check locally.

## Maintainers

- [Publication checklist](publication-checklist.md): the steps for publishing the repository safely, including what to do once the first CI run exists.
- [Screenshots](screenshots.md): which screenshots would help and how to capture them without leaking personal data.
