#!/usr/bin/env bash
# Shared helpers for the db-*.sh scripts (backup, restore, fingerprint, migration rehearsal). Source it; do not run it.
#
# Safety rules these helpers enforce:
#   * They talk to the Docker Compose PostgreSQL service of THIS repository only.
#   * The live database (POSTGRES_DB, default "vehiclerental") is only ever READ (pg_dump, SELECT). Anything that
#     creates, restores into, migrates or drops a database works on a SCRATCH database whose name starts with
#     "vr_rehearsal_", and refuses any other name.
#   * Dropping asks for confirmation unless --yes is given, and never runs unattended without it.
#   * "docker compose down -v" is never used.

set -euo pipefail

# Git Bash on Windows rewrites arguments that look like paths; none of these commands want that.
export MSYS_NO_PATHCONV=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRATCH_PREFIX="vr_rehearsal_"

# Reads KEY=value from .env without executing it (the file holds secrets; it is only parsed, never sourced).
env_value() {
  local key="$1" default="${2:-}" line
  if [[ -f "$ROOT/.env" ]]; then
    line="$(grep -E "^${key}=" "$ROOT/.env" | tail -n 1 || true)"
    if [[ -n "$line" ]]; then
      printf '%s' "${line#*=}" | tr -d '\r'
      return
    fi
  fi
  printf '%s' "$default"
}

PG_USER="$(env_value POSTGRES_USER vehiclerental)"
LIVE_DB="$(env_value POSTGRES_DB vehiclerental)"

compose() { (cd "$ROOT" && docker compose "$@"); }

die() { echo "error: $*" >&2; exit 1; }

require_stack() {
  command -v docker >/dev/null 2>&1 || die "docker is not installed or not on PATH."
  local state
  state="$(compose ps --status running --format '{{.Service}}' 2>/dev/null | grep -x postgres || true)"
  [[ -n "$state" ]] || die "the Compose postgres service is not running. Start it with: docker compose up -d postgres"
}

# psql inside the postgres container, over its local socket (no password needed there). Usage: psql_db DB [psql args...]
psql_db() {
  local db="$1"; shift
  compose exec -T postgres psql -U "$PG_USER" -d "$db" -v ON_ERROR_STOP=1 -At "$@"
}

is_scratch_name() { [[ "$1" =~ ^vr_rehearsal_[0-9]{8}_[0-9]{6}(_[a-z0-9]+)?$ ]]; }

require_scratch_name() {
  is_scratch_name "$1" || die "'$1' is not a scratch database name. Only names like ${SCRATCH_PREFIX}YYYYMMDD_HHMMSS are accepted."
  [[ "$1" != "$LIVE_DB" ]] || die "refusing to touch the live database '$LIVE_DB'."
}

database_exists() { [[ "$(psql_db postgres -c "SELECT 1 FROM pg_database WHERE datname = '$1'")" == "1" ]]; }

# confirm "question" : yes when --yes was given (ASSUME_YES=1) or the user types y; otherwise no.
confirm() {
  if [[ "${ASSUME_YES:-0}" == "1" ]]; then return 0; fi
  if [[ ! -t 0 ]]; then return 1; fi
  local answer
  read -r -p "$1 [y/N] " answer
  [[ "$answer" == "y" || "$answer" == "Y" ]]
}

drop_scratch() {
  local name="$1"
  require_scratch_name "$name"
  database_exists "$name" || { echo "scratch database '$name' does not exist."; return 0; }
  if confirm "Drop scratch database '$name'? This cannot be undone (the live database is not affected)."; then
    psql_db postgres -c "DROP DATABASE \"$name\" WITH (FORCE)" >/dev/null
    echo "dropped scratch database '$name'."
  else
    echo "kept scratch database '$name'. Drop it later with: bash scripts/db-restore.sh --drop $name"
  fi
}
