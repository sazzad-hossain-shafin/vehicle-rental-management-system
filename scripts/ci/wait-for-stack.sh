#!/usr/bin/env bash
# Waits (with a hard time limit) for the Docker Compose stack to become ready, in order:
#   PostgreSQL healthy  ->  the migration job finished successfully  ->  the API healthy  ->  the website healthy
#   (the website is only waited for when the stack has a "web" service).
# It fails FAST if the migration job fails or the API container stops, instead of waiting out the timeout.
#
#   bash scripts/ci/wait-for-stack.sh [TIMEOUT_SECONDS]      (default 240)
set -uo pipefail

timeout="${1:-240}"
deadline=$((SECONDS + timeout))

state_of() { # state_of SERVICE FIELD   (State, Health or ExitCode)
  docker compose ps -a --format "{{.Service}}|{{.State}}|{{.Health}}|{{.ExitCode}}" 2>/dev/null \
    | awk -F'|' -v svc="$1" -v field="$2" '$1==svc { if (field=="State") print $2; else if (field=="Health") print $3; else print $4 }' | head -1
}

echo "Waiting up to ${timeout}s for postgres -> migrate -> api ..."
last=""
while (( SECONDS < deadline )); do
  pg_health="$(state_of postgres Health)"
  mig_state="$(state_of migrate State)"; mig_exit="$(state_of migrate ExitCode)"
  api_state="$(state_of api State)";     api_health="$(state_of api Health)"
  web_state="$(state_of web State)";     web_health="$(state_of web Health)"

  now="postgres=${pg_health:-?} migrate=${mig_state:-?}(exit ${mig_exit:-?}) api=${api_state:-?}/${api_health:-?} web=${web_state:-none}/${web_health:-}"
  [[ "$now" != "$last" ]] && { echo "  [${SECONDS}s] $now"; last="$now"; }

  if [[ "$mig_state" == "exited" && "$mig_exit" != "0" ]]; then
    echo "FAILED: the migration job exited with code $mig_exit"
    exit 1
  fi
  if [[ "$api_state" == "exited" || "$api_state" == "dead" ]]; then
    echo "FAILED: the API container stopped (state: $api_state, exit code: $(state_of api ExitCode))"
    exit 1
  fi
  if [[ "$web_state" == "exited" || "$web_state" == "dead" ]]; then
    echo "FAILED: the website container stopped (state: $web_state)"
    exit 1
  fi
  web_ok=true
  [[ -n "$web_state" && "$web_health" != "healthy" ]] && web_ok=false
  if [[ "$pg_health" == "healthy" && "$mig_state" == "exited" && "$mig_exit" == "0" && "$api_state" == "running" && "$api_health" == "healthy" && "$web_ok" == "true" ]]; then
    echo "Ready: PostgreSQL healthy, migrations applied, API healthy, website healthy if present (after ${SECONDS}s)."
    exit 0
  fi
  sleep 3
done

echo "FAILED: the stack was not ready within ${timeout}s."
exit 1
