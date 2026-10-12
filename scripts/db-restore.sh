#!/usr/bin/env bash
# Restores a backup into a NEW SCRATCH database (never the live one), or drops a scratch database.
#
#   bash scripts/db-restore.sh --file backups/vehiclerental-20261012_101500.dump
#   bash scripts/db-restore.sh --file F --into vr_rehearsal_20261012_101500   # choose the scratch name
#   bash scripts/db-restore.sh --drop vr_rehearsal_20261012_101500            # asks first (or add --yes)
#
# Safety: the target must be named vr_rehearsal_YYYYMMDD_HHMMSS (optionally _suffix) and must not exist yet. The live
# database is never a target. Dropping always asks, unless you pass --yes.
source "$(dirname "${BASH_SOURCE[0]}")/db-common.sh"

file=""
into=""
drop=""
ASSUME_YES=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --file) file="$2"; shift 2 ;;
    --into) into="$2"; shift 2 ;;
    --drop) drop="$2"; shift 2 ;;
    --yes) ASSUME_YES=1; shift ;;
    -h|--help) sed -n '2,11p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

require_stack

if [[ -n "$drop" ]]; then
  drop_scratch "$drop"
  exit 0
fi

[[ -n "$file" && -f "$file" ]] || die "give a backup file with --file (see scripts/db-backup.sh)."
[[ -n "$into" ]] || into="${SCRATCH_PREFIX}$(date +%Y%m%d_%H%M%S)"
require_scratch_name "$into"
database_exists "$into" && die "scratch database '$into' already exists; choose another name or drop it first."

echo "Restoring $file"
echo "  into scratch database: $into   (live database '$LIVE_DB' is not touched)"
psql_db postgres -c "CREATE DATABASE \"$into\"" >/dev/null
# --no-owner/--no-privileges: the scratch copy is owned by the connecting role. Extensions (btree_gist) are in the dump.
compose exec -T postgres pg_restore -U "$PG_USER" -d "$into" --no-owner --no-privileges --exit-on-error < "$file"
echo "Restored. Row counts:"
for table in Users Customers Vehicles Rentals Reservations; do
  printf '  %-13s %s\n' "$table" "$(psql_db "$into" -c "SELECT count(*) FROM \"$table\"")"
done
echo "Next: bash scripts/db-fingerprint.sh $into"
