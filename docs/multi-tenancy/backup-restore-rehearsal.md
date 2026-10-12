# Backup, restore and migration rehearsal

Four small scripts in `scripts/` let you prove a migration on a **copy** of your data before it ever meets the real database. They talk to the Docker Compose PostgreSQL service of this repository.

| Script | What it does | Touches the live database? |
|---|---|---|
| `scripts/db-backup.sh` | Writes a custom-format `pg_dump` to `backups/` and checks it can be read back | reads it |
| `scripts/db-restore.sh` | Restores a backup into a **new scratch database**, or drops a scratch database | no |
| `scripts/db-fingerprint.sh` | Prints per-table row counts and a hash of every old column (optionally with each row's `xmin`) | reads it |
| `scripts/db-rehearse-migration.sh` | Backup, restore to scratch, compare, apply the migrations, compare, roll back, compare | reads it |

Run them from the repository root with Bash (Git Bash works on Windows). The Compose `postgres` service must be running; `.env` supplies the database name, user and password (it is read, never printed).

## Safety properties

- The live database (`POSTGRES_DB`, default `vehiclerental`) is only ever read. Creating, restoring into, migrating and dropping happen only in a scratch database named `vr_rehearsal_YYYYMMDD_HHMMSS`; any other name is refused, and so is the live database.
- Dropping asks for confirmation. Without a terminal, or without `--yes`, the scratch database is kept and the drop command is printed.
- `docker compose down -v` is never used. Nothing here deletes the Compose volume.
- The migration bundle is pointed at the scratch database twice (the service's environment value and the bundle's own `--connection` option), and the live migration history is compared before and after, so a mistake in one mechanism cannot reach the live database unnoticed.
- A failed or interrupted backup removes its partial file; database names passed to the scripts are validated.
- Backups hold real data (accounts, bookings). `backups/` is ignored by Git. Do not commit, upload or share a dump; treat it like the database.

## The usual run

```bash
bash scripts/db-rehearse-migration.sh --yes
```

Each step prints `PASS` or `FAIL` and the script exits non-zero on a failure:

1. take a backup of the live database;
2. restore it into a scratch database;
3. check the copy equals the live data (counts and hashes of every old column);
4. build the migration bundle from the current source (`docker compose build migrate`, which does not touch any running container);
5. apply the migrations to the scratch database with the same bundle the stack uses, and time it;
6. check every existing row **and its `xmin` row version** is unchanged, the new tables exist, and they and every tenant column are empty or NULL;
7. roll the migration back on the scratch database (`--rollback-to`, default `AddReservations`);
8. check data and row versions are identical to before;
9. check the live database was not migrated and its data did not change.

Useful options: `--backup FILE` (use an existing backup), `--no-build` (reuse the built image), `--rollback-to NAME`. Without `--yes` the script asks before dropping the scratch database.

You can also run the steps by hand:

```bash
bash scripts/db-backup.sh
bash scripts/db-restore.sh --file backups/vehiclerental-20261012_101500.dump      # prints the scratch name
bash scripts/db-fingerprint.sh vr_rehearsal_20261012_101530 --xmin
bash scripts/db-restore.sh --drop vr_rehearsal_20261012_101530                    # asks first
```

`--xmin` is only meaningful within one database over time: a dump and restore assigns new row versions, so compare `xmin` fingerprints taken before and after a migration on the **same** copy.

## Applying the real migration

The Compose `migrate` job applies pending migrations when you start the stack. For a real database that matters: take a backup first (`bash scripts/db-backup.sh`), rehearse, and only then start the stack. Rolling back the 14A-3 migration on the real database is refused once tenant data exists; see [the expand step](expand-and-cutover.md#rolling-back).

## Recorded rehearsal: 14A-3, 12 October 2026

Run against the local development database (demo data created while testing the website: 27 users, 26 customers, 18 vehicles, 24 rentals, 73 reservations), restored into a scratch database. All checks passed:

| Check | Result |
|---|---|
| Restored copy equals the live data | PASS |
| Migration `AddCompanyTenancyExpand` applied (migration job) | PASS, 4 seconds including the bundle starting |
| Every existing row and its `xmin` unchanged after the migration | PASS |
| Three new tables exist; they and all tenant columns are empty or NULL | PASS |
| Rollback to `AddReservations` | PASS |
| Data and `xmin` identical after the rollback; new tables gone | PASS |
| Live database not migrated and its data unchanged | PASS |

The live development database was **not** migrated by the rehearsal. The migration will be applied to it by the normal `migrate` job when the stack next starts with this code.

## Limits

- It rehearses the data and schema, not load: concurrent bookings during a migration are covered by the lock-timeout test, not by this script.
- The copy is as large as your database; a production-sized dataset would show real timings.
- The scripts target the Compose stack. Another PostgreSQL server would need the same steps with its own tooling.
