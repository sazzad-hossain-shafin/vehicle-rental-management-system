# Multi-company tenancy: supporting documents

These documents support [ADR 007](../architecture/007-multi-company-tenancy.md). They describe the **planned** design for running several independent rental companies on one platform. Nothing here is implemented yet; the running system is still single-company.

- [Data ownership and authorization](data-ownership-and-authorization.md): who owns each table, the role capability matrix, tenant resolution and invitations.
- [Migration strategy](migration-strategy.md): expand, backfill, verify, contract, and how to roll back.
- [Threat model](threat-model.md): what could go wrong between tenants, the control for each, and the test that proves it.
- [Acceptance criteria](acceptance-criteria.md): what must be true before each step may merge.

Status of the work:

| Step | State |
|---|---|
| Architecture decision (ADR 007) | recorded |
| RLS and connection-pooling proof of concept | next pull request |
| Everything else | not started, each step needs approval |
