# Multi-company tenancy: supporting documents

These documents support [ADR 007](../architecture/007-multi-company-tenancy.md). They describe the **planned** design for running several independent rental companies on one platform. Nothing here is implemented yet; the running system is still single-company.

- [Data ownership and authorization](data-ownership-and-authorization.md): who owns each table, the role capability matrix, tenant resolution and invitations.
- [Migration strategy](migration-strategy.md): expand, backfill, verify, contract, and how to roll back.
- [Threat model](threat-model.md): what could go wrong between tenants, the control for each, and the test that proves it.
- [Acceptance criteria](acceptance-criteria.md): what must be true before each step may merge.
- [The expand step, the write gap, and backfill and cutover](expand-and-cutover.md): what 14A-3 added, why each index and foreign key is or is not there, and the safe, repeatable strategy for the later steps.
- [Backup, restore and migration rehearsal](backup-restore-rehearsal.md): scripts that prove a migration on a copy of the data first.

Status of the work:

| Step | State |
|---|---|
| Architecture decision (ADR 007) | recorded |
| RLS and connection-pooling proof of concept | done, successful: [results](../../tests/VehicleRental.TenantIsolation.Spike/README.md) |
| Schema expand (14A-3) | implemented in a pull request awaiting review: [what it added](expand-and-cutover.md) |
| Everything after (tenant context, backfill, scoping, contract, authorization, frontend) | not started, each step needs approval |
