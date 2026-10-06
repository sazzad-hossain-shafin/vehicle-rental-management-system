# Architecture overview

The solution is a set of .NET projects whose references point inward, toward the domain. Names below match the projects in `src/`.

## Dependencies

```mermaid
flowchart TB
    Api["Api"] --> App["Application"]
    Api --> Infra["Infrastructure"]
    Console["Console"] --> App
    Console --> Infra
    Infra --> App
    App --> Domain["Domain"]
    Infra --> Domain
```

An arrow means "references". The Domain references nothing. The Application layer references only the Domain, plus dependency-injection abstractions. Infrastructure implements the Application layer's repository and unit-of-work interfaces, and the Api and Console projects are the entry points that wire implementations to interfaces.

| Layer | Owns | Must not contain |
|---|---|---|
| Domain | Entities, validation, rental lifecycle, pricing strategies and policy | Framework, database or HTTP types |
| Application | Use-case services, DTOs, repository and unit-of-work interfaces, paging, application errors | EF Core, Npgsql, ASP.NET Core |
| Infrastructure | EF Core mapping and migrations, repositories, Identity accounts, JWT issuing, database health check | Business rules |
| Api | Endpoints, request validation, authentication and authorization policies, Problem Details, OpenAPI | Business rules, data access |
| Console | A text-menu client for the same use cases | Business rules |

## Running system (Docker Compose)

```mermaid
flowchart LR
    Client["HTTP client"] -->|"127.0.0.1:8080"| API["api container<br/>read-only, non-root"]
    Migrate["migrate container<br/>one-shot"] --> PG
    API --> PG[("postgres container<br/>named volume pgdata")]
```

PostgreSQL starts and becomes healthy, then the one-shot `migrate` service applies the EF Core migrations and exits, then the API starts. The API never changes the schema itself. See [Docker](../docker.md).

## Continuous integration

```mermaid
flowchart LR
    Q["Quality checks<br/>hygiene, format, vulnerabilities"]
    B["Build and test<br/>PostgreSQL service container"] --> D["Docker<br/>compose build, start, smoke test"]
```

Quality checks and Build and test run in parallel; the Docker job starts after Build and test succeeds. See [CI](../ci.md). The workflow runs on GitHub-hosted runners.

## Decision records

- [ADR 001: PostgreSQL persistence with EF Core](001-postgresql-persistence.md)
- [ADR 002: Docker development environment](002-docker-development-environment.md)
- [ADR 003: Authentication, authorization and customer ownership](003-authentication-and-authorization.md)
