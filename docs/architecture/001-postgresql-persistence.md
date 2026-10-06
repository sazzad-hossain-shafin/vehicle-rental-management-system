# ADR 001: PostgreSQL persistence with EF Core

Status: accepted

## Context

Until now all data lived in memory and disappeared when the console app exited. The next steps (a web API, booking) need durable, shared, transactional storage.

## Decisions

**PostgreSQL with EF Core (Npgsql).** The data is relational (vehicles, customers, rentals) and needs unique constraints, foreign keys and transactions. PostgreSQL is open source and widely deployed, and EF Core's provider for it is mature.

**Persistence lives in `VehicleRental.Infrastructure`.** Domain and Application have no reference to EF Core or Npgsql. The Application layer defines the repository and unit-of-work interfaces, and Infrastructure implements them. Mapping is done with the Fluent API, so the domain classes carry no persistence attributes. The only concession is a private parameterless constructor on each entity, which EF Core uses to rebuild stored objects without re-running validation.

**Identifiers: a generated `Guid` plus a business key.**
- `Vehicle.Id`, `Customer.Id` and `Rental.Id` are UUIDs created in the domain (version 7, so they are roughly time-ordered, which suits database indexes). They never change and are what foreign keys and, later, API URLs use.
- `Vehicle.RegistrationNumber` and `Customer.CustomerNumber` are business keys: the identifiers people type and read. They are unique, normalized (trimmed, upper-case) in the domain, and enforced by unique indexes.
- Why not make the business key the primary key? Business identifiers get corrected and reformatted; a primary key should not. Why not database-generated integers? They leak counts, need a database round trip before an entity has an identity, and make it awkward to create entities in the domain.

**Enums are stored as text** (`Active`, `Available`, ...). The data stays readable, and reordering an enum in code cannot silently change stored meaning.

**Money is `numeric(precision, 2)`**, never floating point.

**Rental history is protected.** Foreign keys use `ON DELETE RESTRICT`, so deleting a vehicle or customer that has rentals is refused rather than cascading. The price of a rental is stored on the rental when it starts, so history never depends on current vehicle or pricing data.

**Concurrency: optimistic, using PostgreSQL's row version.**
- Vehicles and rentals use the `xmin` system column as an EF Core concurrency token. If a row changed after a request read it, saving fails and the request gets a `ConflictException`.
- A partial unique index (`UX_Rentals_ActiveRentalPerVehicle`) lets the database hold at most one active rental per vehicle, whatever the application does.
- Together these guarantee that two concurrent requests cannot both successfully rent or both complete the same rental. The loser sees a conflict and nothing it did is saved.
- What it does not do: it does not queue or retry requests (the caller decides whether to retry), it does not protect rules that span several rows beyond the constraints above, and it is not a distributed lock.

**Transactions.** `IUnitOfWork` is implemented with `DbContext.SaveChangesAsync`, which commits all pending changes in one database transaction. A rental, its new customer and the vehicle's status change are saved together or not at all.

**Migrations are applied explicitly.** The console app checks the schema at startup and tells the developer to run `dotnet ef database update`; it never changes the schema itself. Migrations live in the Infrastructure project, and the EF Core tool is pinned as a repository-local tool.

## Consequences

- A PostgreSQL server is needed to run the app. The model and migration tests run without one; the integration tests run when `VEHICLERENTAL_TEST_CONNECTION` points at a server.
- Application tests still use in-memory fakes, kept in the test project, so they stay fast and need no database.
- Docker (a ready-made local database) and the API's dependency injection are planned for later phases.

## Later updates

The Docker environment and the API's dependency injection mentioned above were added afterwards: see [ADR 002](002-docker-development-environment.md) and [ADR 003](003-authentication-and-authorization.md). The decisions in this record are unchanged.
