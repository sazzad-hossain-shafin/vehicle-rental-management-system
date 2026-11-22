# Multi-company tenancy: supporting documents

These documents support [ADR 007](../architecture/007-multi-company-tenancy.md). They describe the **planned** design for running several independent rental companies on one platform. Nothing here is implemented yet; the running system is still single-company.

- [Data ownership and authorization](data-ownership-and-authorization.md): who owns each table, the role capability matrix, tenant resolution and invitations.
- [Migration strategy](migration-strategy.md): expand, backfill, verify, contract, and how to roll back.
- [Threat model](threat-model.md): what could go wrong between tenants, the control for each, and the test that proves it.
- [Acceptance criteria](acceptance-criteria.md): what must be true before each step may merge.
- [The expand step, the write gap, and backfill and cutover](expand-and-cutover.md): what 14A-3 added, why each index and foreign key is or is not there, and the safe, repeatable strategy for the later steps.
- [Backup, restore and migration rehearsal](backup-restore-rehearsal.md): scripts that prove a migration on a copy of the data first.
- [Tenant infrastructure](tenant-infrastructure.md): the tenant context, the transaction-local tenant setting, the unit of work joining the tenant transaction, the restricted runtime role and its startup check (built and switched off).
- [Runbook: the restricted runtime role](runtime-role-runbook.md): upgrading `.env`, checking the role, rolling back and rotating the password.
- [Backfill readiness review](backfill-readiness-review.md): the 14A-5 backfill design checked against the code, the gaps found, and the acceptance criteria (review, not implemented).

Status of the work:

| Step | State |
|---|---|
| Architecture decision (ADR 007) | recorded |
| RLS and connection-pooling proof of concept | done, successful: [results](../../tests/VehicleRental.TenantIsolation.Spike/README.md) |
| Schema expand (14A-3) | implemented in a pull request awaiting review: [what it added](expand-and-cutover.md) |
| Tenant infrastructure and the restricted runtime role (14A-4) | implemented in a pull request awaiting review: [what it added](tenant-infrastructure.md) |
| Everything after (backfill, scoping, contract, authorization, frontend) | not started, each step needs approval |
