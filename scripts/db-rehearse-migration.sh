#!/usr/bin/env bash
# Rehearses applying (and rolling back) the pending EF Core migrations against a COPY of the live database.
# The live database is only read. Everything else happens in a scratch database named vr_rehearsal_YYYYMMDD_HHMMSS.
#
#   bash scripts/db-rehearse-migration.sh                     # backup, restore to scratch, migrate, compare, roll back
#   bash scripts/db-rehearse-migration.sh --backup FILE       # use an existing backup instead of taking one
#   bash scripts/db-rehearse-migration.sh --rollback-to NAME  # migration to roll back to (default: AddReservations)
#   bash scripts/db-rehearse-migration.sh --no-build          # reuse the already built migrate image
#   bash scripts/db-rehearse-migration.sh --yes               # also drop the scratch database at the end, without asking
#
# Steps, each printed as PASS or FAIL (the script exits non-zero on any FAIL):
#   1  back up the live database                      (read only)
#   2  restore the backup into a scratch database     (new database)
#   3  restored data equals live data                 (row counts and hashes of every old column)
#   4  build the migration bundle from the current source
#   5  apply the migrations to the scratch database   (with the same bundle the Docker stack uses)
#   6  existing rows are unchanged, row versions (xmin) included; new tables exist and are empty
#   7  roll the migration back on the scratch database
#   8  data and row versions are still unchanged
#   9  the live database was never migrated
# Finally the scratch database is dropped if you confirm (or pass --yes), otherwise it is kept for inspection.
source "$(dirname "${BASH_SOURCE[0]}")/db-common.sh"

backup_file=""
rollback_to="AddReservations"
build=1
ASSUME_YES=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --backup) backup_file="$2"; shift 2 ;;
    --rollback-to) rollback_to="$2"; shift 2 ;;
    --no-build) build=0; shift ;;
    --yes) ASSUME_YES=1; shift ;;
    -h|--help) sed -n '2,22p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

require_stack
scratch="${SCRATCH_PREFIX}$(date +%Y%m%d_%H%M%S)"
require_scratch_name "$scratch"
password="$(env_value POSTGRES_PASSWORD)"
[[ -n "$password" ]] || die "POSTGRES_PASSWORD is not set in .env (run scripts/init-env.sh first)."
failures=0

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*"; failures=$((failures + 1)); }

fingerprint() { bash "$ROOT/scripts/db-fingerprint.sh" "$@"; }

run_bundle() {
  # The bundle is the migrate image's entrypoint; arguments after the service name are passed to it (a target migration).
  # The scratch connection string replaces the live one for this single run. The password never appears in the output.
  compose run --rm --no-deps \
    -e "ConnectionStrings__VehicleRentalDatabase=Host=postgres;Port=5432;Database=${scratch};Username=${PG_USER};Password=${password};GSS Encryption Mode=Disable" \
    migrate "$@"
}

echo "== Target =="
echo "live database (read only): $LIVE_DB"
echo "scratch database:          $scratch"
echo "rolling back to:           $rollback_to"
echo

echo "== 1. Backup =="
if [[ -z "$backup_file" ]]; then
  backup_output="$(bash "$ROOT/scripts/db-backup.sh")"
  echo "$backup_output"
  backup_file="$(printf '%s\n' "$backup_output" | sed -n 's/^Backup written: //p')"
fi
[[ -f "$backup_file" ]] || die "no backup file."
pass "backup available: $backup_file"
echo

echo "== 2. Restore into the scratch database =="
bash "$ROOT/scripts/db-restore.sh" --file "$backup_file" --into "$scratch"
pass "restored into $scratch"
echo

echo "== 3. Restored data equals live data =="
live_fp="$(fingerprint "$LIVE_DB")"
copy_fp="$(fingerprint "$scratch")"
if [[ "$live_fp" == "$copy_fp" ]]; then pass "scratch copy matches the live database"; else
  echo "--- live"; echo "$live_fp"; echo "--- scratch"; echo "$copy_fp"
  fail "scratch copy differs from the live database (was the application writing during the rehearsal?)"
fi
echo

if [[ "$build" == "1" ]]; then
  echo "== 4. Build the migration bundle =="
  compose build migrate
  pass "migrate image built from the current source"
else
  echo "== 4. Build skipped (--no-build) =="
fi
echo

before_x="$(fingerprint "$scratch" --xmin)"

echo "== 5. Apply the migrations to the scratch database =="
started=$SECONDS
run_bundle
echo "migration took $((SECONDS - started)) s"
pass "migrations applied to $scratch"
echo

echo "== 6. Existing data unchanged, new structures present and empty =="
after_x="$(fingerprint "$scratch" --xmin)"
if [[ "$before_x" == "$after_x" ]]; then pass "every existing row (and its xmin row version) is unchanged"; else
  echo "--- before"; echo "$before_x"; echo "--- after"; echo "$after_x"
  fail "existing data changed during the migration"
fi
new_tables="$(psql_db "$scratch" -c "SELECT count(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('Companies','CompanyMemberships','CompanyCustomers')")"
if [[ "$new_tables" == "3" ]]; then pass "the three new tables exist"; else fail "expected 3 new tables, found $new_tables"; fi
new_rows="$(psql_db "$scratch" -c "SELECT (SELECT count(*) FROM \"Companies\") + (SELECT count(*) FROM \"CompanyMemberships\") + (SELECT count(*) FROM \"CompanyCustomers\") + (SELECT count(*) FROM \"Vehicles\" WHERE \"CompanyId\" IS NOT NULL) + (SELECT count(*) FROM \"Reservations\" WHERE \"CompanyId\" IS NOT NULL OR \"CompanyCustomerId\" IS NOT NULL) + (SELECT count(*) FROM \"Rentals\" WHERE \"CompanyId\" IS NOT NULL OR \"CompanyCustomerId\" IS NOT NULL)")"
if [[ "$new_rows" == "0" ]]; then pass "new tables are empty and every tenant column is NULL"; else fail "$new_rows tenant values exist"; fi
echo

echo "== 7. Roll the migration back on the scratch database =="
run_bundle "$rollback_to"
pass "rolled back to $rollback_to"
echo

echo "== 8. Data and row versions after the rollback =="
rolled_x="$(fingerprint "$scratch" --xmin)"
if [[ "$before_x" == "$rolled_x" ]]; then pass "data and xmin identical to before the migration"; else
  echo "--- before"; echo "$before_x"; echo "--- after rollback"; echo "$rolled_x"
  fail "data differs after rollback"
fi
left="$(psql_db "$scratch" -c "SELECT count(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('Companies','CompanyMemberships','CompanyCustomers')")"
if [[ "$left" == "0" ]]; then pass "the new tables are gone again"; else fail "$left new tables remain"; fi
echo

echo "== 9. The live database was never migrated =="
live_applied="$(psql_db "$LIVE_DB" -c "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%AddCompanyTenancyExpand'")"
live_after="$(fingerprint "$LIVE_DB")"
if [[ "$live_applied" == "0" ]]; then pass "AddCompanyTenancyExpand is not in the live migration history"; else fail "the live database has the migration applied (rehearsal did not cause this; it was applied separately)"; fi
if [[ "$live_after" == "$live_fp" ]]; then pass "live data unchanged by the rehearsal"; else fail "live data changed during the rehearsal (the application may have been writing)"; fi
echo

echo "== Cleanup =="
drop_scratch "$scratch"
echo

if [[ "$failures" -eq 0 ]]; then
  echo "Rehearsal PASSED."
else
  echo "Rehearsal FAILED: $failures check(s) failed."
  exit 1
fi
