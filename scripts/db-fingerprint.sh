#!/usr/bin/env bash
# Prints a fingerprint of the existing business tables: per table, the row count and one hash over every row's OLD
# columns. Two databases with the same fingerprint hold the same data in those tables. READ ONLY.
#
#   bash scripts/db-fingerprint.sh                  # the live database
#   bash scripts/db-fingerprint.sh vr_rehearsal_... # a scratch database
#   bash scripts/db-fingerprint.sh DB --xmin        # also include each row's version (xmin)
#
# The tenant columns added by Phase 14A-3 (CompanyId, CompanyCustomerId) are removed from each row before hashing, so a
# database can be compared before and after that migration. --xmin is only meaningful within ONE database over time
# (a dump and restore assigns new row versions), so use it for "before migrate" against "after migrate" on the same copy.
source "$(dirname "${BASH_SOURCE[0]}")/db-common.sh"

db="$LIVE_DB"
with_xmin=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --xmin) with_xmin=1; shift ;;
    -h|--help) sed -n '2,11p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) db="$1"; shift ;;
  esac
done

[[ "$db" =~ ^[A-Za-z0-9_]+$ ]] || die "'$db' is not a valid database name."
require_stack
database_exists "$db" || die "database '$db' does not exist."

tables=(Users Roles UserRoles Customers Vehicles Rentals Reservations)
for table in "${tables[@]}"; do
  if [[ "$with_xmin" == "1" ]]; then
    expression="xmin::text || ':' || j"
  else
    expression="j"
  fi
  # The table must exist; an empty table gets the hash 'empty'.
  psql_db "$db" -F ' ' -c "
    SELECT '$table', count(*), coalesce(md5(string_agg($expression, '|' ORDER BY j)), 'empty')
    FROM (SELECT xmin, (to_jsonb(t) - 'CompanyId' - 'CompanyCustomerId')::text AS j FROM \"$table\" t) s"
done
