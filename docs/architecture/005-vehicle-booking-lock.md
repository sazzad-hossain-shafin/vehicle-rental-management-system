# ADR 005: Serialising bookings of one vehicle with a database row lock

Status: accepted

## Context

[ADR 004](004-reservations.md) closed reservation-versus-reservation double booking with a PostgreSQL exclusion constraint. It could not close one case: a walk-in **rental** and a **reservation** for the same vehicle and overlapping days, submitted at the same moment. They live in different tables, so no single constraint sees both. Each request checked "is the vehicle free?", both saw "yes", and both were saved. A test that raced the two reproduced it on the first attempt: one rental and one overlapping reservation in the database.

## Decision

**Every operation that creates a claim on a vehicle first takes a row lock on that vehicle, inside its transaction, and only then checks availability.** Creating a reservation and starting a rental both call `IUnitOfWork.LockVehicleAsync(vehicleId)`, which starts an explicit transaction and runs `SELECT "Id" FROM "Vehicles" WHERE "Id" = @id FOR UPDATE`. `SaveChangesAsync` commits that transaction; if the request fails, or the unit of work is disposed without saving, the transaction rolls back and the lock is released.

Why this works: PostgreSQL grants the lock to one transaction at a time, so bookings of one vehicle run one after another. The second request waits, acquires the lock after the first has committed, and its availability check (a fresh query, under the default `READ COMMITTED` isolation) now sees the first request's rental or reservation, so it reports a normal 409 conflict.

**Why a row lock and not the alternatives**

- *In-process locks* (`lock`, `SemaphoreSlim`) only coordinate requests handled by the same process. Two API instances behind a load balancer would not see each other. The lock is in the database, so it works across any number of instances. The code base contains no in-memory locking.
- *A single table or trigger for all claims* would move rentals and reservations into one structure with one exclusion constraint. That is a large rewrite of working, tested code for a case the lock already closes.
- *Serializable transactions* would make every write path retry-aware and still leave the application to express the overlap rule.
- *Advisory locks* work, but a row lock on the vehicle ties the lock to the thing being protected and needs no key scheme.

**Bounded waiting.** The transaction sets `lock_timeout = 5s`. A request stuck behind another one gives up with a 409 ("being booked by another request, please try again") instead of hanging; PostgreSQL error `55P03` is translated like the other race outcomes.

**Locks are per vehicle.** Bookings of different vehicles never wait for each other. Optimistic concurrency (the `xmin` row version) and the unique indexes stay in place as a second layer: a row lock does not change `xmin`, so the existing checks behave as before.

## Consequences

- Rental-versus-reservation, reservation-versus-reservation and rental-versus-rental races are all closed by the database, across instances. Tests race all combinations, including two independent API hosts on one database.
- A booking holds a vehicle row lock for the short time between the check and the save. Different vehicles are unaffected, and a stuck request cannot block others for more than the lock timeout.
- The guarantee depends on every claim-creating code path calling `LockVehicleAsync`. Reservation-versus-reservation overlap remains protected by the exclusion constraint even for SQL that bypasses the application; rental-versus-reservation overlap is not, because it has no single constraint. Direct writes to the tables outside the application are out of scope.
- Pickup does not take the lock: it moves an existing claim from a reservation to a rental inside the reservation's own dates, and the reservation's row version plus the unique rental index already allow exactly one pickup.
