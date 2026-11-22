# Runbook: the restricted runtime role

How to set up, check, roll back and clean up the restricted PostgreSQL role the API connects as. Background: [tenant infrastructure](tenant-infrastructure.md).

## Upgrading an existing `.env`

Docker Compose now needs `POSTGRES_APP_PASSWORD`. A `.env` created before this change does not have it, and Compose refuses to render the file until it is added, with the message "Set POSTGRES_APP_PASSWORD in .env". Add only what is missing; every existing value (including secrets) is left exactly as it is, and only the names are printed:

```bash
bash scripts/init-env.sh --upgrade        # Windows PowerShell: .\scripts\init-env.ps1 -Upgrade
```

Until that is done, `docker compose ps`, `up` and the like fail with that message (the `db-*.sh` helper scripts keep working: they supply a placeholder for commands that never use the password).

## What happens when the stack starts

```text
postgres (healthy)  ->  migrate (owner, applies migrations, exits)  ->  db-roles (owner, provisions the runtime role, exits)  ->  api (runtime role, role check Enforce)  ->  web
```

`db-roles` runs on every start and is safe to repeat. Its output is empty on success. If it fails, the API does not start (`wait-for-stack.sh` reports the failure). The usual causes: `POSTGRES_APP_PASSWORD` shorter than 16 characters or containing characters other than letters and digits, `POSTGRES_APP_USER` equal to the owner, or an object owned by the runtime role.

The first start after upgrading creates the role. The existing data, tables and demo records are untouched: the role is created beside them and granted access.

## Checking it

```bash
docker compose ps                                   # db-roles shows "exited (0)", api "healthy"
curl -s http://localhost:8080/health                # "The database role is safe."
```

Directly in PostgreSQL (as the owner):

```sql
SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls, rolinherit
FROM pg_roles WHERE rolname = 'vehiclerental_app';                       -- all false
SELECT count(*) FROM pg_auth_members m JOIN pg_roles u ON u.oid = m.member WHERE u.rolname = 'vehiclerental_app';  -- 0
```

## Rollback

There are two situations; neither drops anything.

**1. Normal rollback: revert the pull request.** Reverting restores the previous `compose.yaml` and the API connects as the owner again, as before. The runtime role stays in the database, unused and harmless. No data or schema changed, so nothing else needs undoing. This was rehearsed on an isolated stack: starting the previous compose file over the same volume brought the API up as the owner and the smoke test passed, and starting the new file again returned it to the runtime role.

**2. Emergency: the API will not start because of the role.** For example a privilege problem the tests did not foresee. The quickest safe step is to keep the restricted role and relax only the check, so the API starts and you can read what is wrong: set `Tenancy__DatabaseRoleCheck: Warn` for the `api` service (a `compose.override.yaml` entry), start it, read the log and `/health`, fix the grant by re-running `db-roles` (`docker compose run --rm db-roles`), then remove the override. Outside the Development environment the API logs that `Warn` is a temporary emergency setting. `Off` is not an option there: the API refuses to start with it.

If the **table owner's credentials** must be used for the API in an emergency (set `ConnectionStrings__VehicleRentalDatabase` to the owner string), treat it as a temporary measure with these consequences: the API then runs with the power to change or drop the schema, Row-Level Security (when it exists) would not bind it, and the role check will warn (or refuse in `Enforce`). **Switching the API back to a privileged owner is not a normal or long-term solution.** Undo it as soon as the cause is fixed.

Nothing in this runbook drops a role, a table, a database or a volume.

## Removing the role (only if you decide to, by hand)

Nothing does this automatically. After the API has stopped using it:

```sql
REASSIGN OWNED BY vehiclerental_app TO vehiclerental;   -- it owns nothing, so this is a no-op, but it is the safe order
DROP OWNED BY vehiclerental_app;                        -- revokes its privileges in this database
DROP ROLE vehiclerental_app;
```

Run these as the owner, in the application database.

## Rotating the runtime password

Change `POSTGRES_APP_PASSWORD` in `.env` and start the stack again (or run `docker compose run --rm db-roles` then restart the API). The script re-sets the password on every run. Keep it different from `POSTGRES_PASSWORD`.

## Real (non-Compose) databases

Run `scripts/sql/provision-runtime-role.sql` as the owner of the tables, after the migrations, with the inputs set as custom settings, for example:

```bash
PGOPTIONS="-c provision.app_user=vehiclerental_app -c provision.app_password=$APP_PASSWORD" \
  psql -v ON_ERROR_STOP=1 --no-psqlrc -f scripts/sql/provision-runtime-role.sql
```

Do not put the password on a command line that is logged; use the environment as above. Re-run it after every migration only if the owner did not create the tables itself (otherwise the default privileges already cover them).
