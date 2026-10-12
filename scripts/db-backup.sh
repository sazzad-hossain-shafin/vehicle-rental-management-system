#!/usr/bin/env bash
# Takes a backup (a custom-format pg_dump) of the live Docker Compose database. READ ONLY: it never changes the database.
#
#   bash scripts/db-backup.sh                 # writes backups/vehiclerental-YYYYMMDD_HHMMSS.dump
#   bash scripts/db-backup.sh --out some/dir  # writes into another directory
#
# The file holds real data (accounts, bookings): it is created readable by you only, and the backups/ directory is
# ignored by Git. Never commit or share it. Restore it into a SCRATCH database with scripts/db-restore.sh.
source "$(dirname "${BASH_SOURCE[0]}")/db-common.sh"

out_dir="$ROOT/backups"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) out_dir="$2"; shift 2 ;;
    -h|--help) sed -n '2,10p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

require_stack
mkdir -p "$out_dir"
timestamp="$(date +%Y%m%d_%H%M%S)"
file="$out_dir/${LIVE_DB}-${timestamp}.dump"

echo "Database to back up (read only): $LIVE_DB"
( umask 077; compose exec -T postgres pg_dump -U "$PG_USER" -d "$LIVE_DB" --format=custom --no-owner > "$file" )

[[ -s "$file" ]] || { rm -f "$file"; die "the backup is empty; nothing was written."; }

# A backup that cannot be read back is not a backup: list its table of contents.
entries="$(compose exec -T postgres pg_restore --list < "$file" | grep -c . || true)"
[[ "$entries" -gt 10 ]] || die "the backup does not look valid (only $entries entries): $file"

if command -v sha256sum >/dev/null 2>&1; then
  hash="$(sha256sum "$file" | cut -d' ' -f1)"
else
  hash="(sha256sum not available)"
fi

echo "Backup written: $file"
echo "  size:    $(wc -c < "$file") bytes"
echo "  entries: $entries"
echo "  sha256:  $hash"
