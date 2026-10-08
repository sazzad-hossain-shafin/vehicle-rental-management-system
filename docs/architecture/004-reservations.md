# ADR 004: Reservations, date availability and pickup

Status: accepted

## Context

Customers could sign in and read their own data, but only staff could start a rental, and only for today. The system needed a customer-facing booking workflow for future dates without giving customers any staff permissions and without allowing a vehicle to be double booked.

## Decisions

**A separate `Reservation` entity, not a future-dated `Rental`.** A reservation is a promise and a price quote; a rental is a vehicle that has left the lot. Rentals keep their rule that the vehicle is rented from the day they start. A reservation never changes the vehicle's `Available` / `Rented` status. Keeping the two apart means "is the vehicle out right now?" and "is it free on those dates?" stay different questions.

**Half-open date interval `[start, end)`.** It matches how rentals already bill (the start day counts, the return day does not), makes back-to-back bookings natural, and maps directly onto PostgreSQL's `daterange(start, end, '[)')`.

**The price is quoted once and stored.** The existing `PricingPolicy` picks the strategy; the daily rate, billable days, description and total are stored on the reservation. Pickup creates the rental from that quote instead of pricing again, so a late pickup does not silently change the price. Pricing strategies remain stateless and are not stored on the vehicle.

**A database exclusion constraint prevents double booking.** An application check gives a good error message but cannot stop two simultaneous requests. A PostgreSQL exclusion constraint on `(VehicleId =, daterange(StartDate, EndDate) &&) WHERE Status = 'Active'` can. Alternatives considered: a unique index per day (needs a row per vehicle-day), serializable transactions (retries everywhere, and still needs the application to implement the overlap test), and advisory locks (application-level locking that other writers can bypass). The constraint is the smallest mechanism that holds whatever code writes the row. It requires the `btree_gist` extension, which the migration creates explicitly; the extension is trusted, so no superuser is needed.

**Conflicts, deadlocks and the constraint all become `409`.** The unit of work translates exclusion violations, unique violations, concurrency-token failures and PostgreSQL deadlock / serialization aborts into the existing `ConflictException`. Without the deadlock case, three or more simultaneous overlapping bookings could surface a server error for the losers.

**Availability is one SQL query behind an Application interface.** `IVehicleAvailabilityQuery` combines reservations and active rentals with `NOT EXISTS` conditions and pages in the database. An overdue rental still occupies today.

**Customer endpoints under `/me`, desk endpoints under `/reservations`.** Customer booking takes no customer ID: it comes from the signed token, so there is nothing to tamper with, and another customer's reservation looks like a missing one. A new `ReservationManage` policy (Staff, Admin) guards the desk endpoints; customers use the existing `CustomerSelfService` policy. A customer cannot request the promotional discount.

**Pickup is one transaction in one action.** `POST /reservations/{id}/pickup` runs the domain's `Reservation.PickUp`, which validates the window, marks the vehicle rented, builds the rental from the quote and marks the reservation fulfilled. The service saves them together. The reservation's row version and a unique index on `RentalId` allow only one concurrent pickup.

**Walk-in rentals respect reservations.** `RentalService` refuses a rental whose days overlap an active reservation. This is an application check only (see Consequences).

## Consequences

- A walk-in rental and a reservation live in different tables, so no single constraint covers both. When this ADR was written that left a race between a simultaneous walk-in and reservation; a regression test reproduced it, and [ADR 005](005-vehicle-booking-lock.md) closes it with a per-vehicle row lock.
- Active reservations whose end date has passed are not cancelled automatically; they are flagged `isExpired` and never block anything.
- The migration adds the `btree_gist` extension to the database and leaves it installed when rolled back.
- Everything is whole days in the server's local date; times of day and time zones are not modelled.

See [reservations](../reservations.md) for the behaviour and endpoints.
