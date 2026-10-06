#!/usr/bin/env bash
# Creates .env from .env.example with freshly generated random secrets, for Docker Compose.
#
# Fills in POSTGRES_PASSWORD, JWT_SIGNING_KEY and the development admin (admin@example.test with a random
# password). An existing .env is never replaced unless you pass --force. The secrets are written only to .env
# (which Git ignores); they are not printed.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
example="$root/.env.example"
target="$root/.env"

if [[ -e "$target" && "${1:-}" != "--force" ]]; then
  echo ".env already exists, so it was left unchanged. Run with --force to replace it."
  exit 0
fi

random_hex() { head -c "$1" /dev/urandom | od -An -tx1 | tr -d ' \n'; }

postgres_password="$(random_hex 24)"
jwt_key="$(head -c 48 /dev/urandom | base64 | tr -d '\n')"
admin_password="Aa1-$(random_hex 10)"

while IFS= read -r line || [[ -n "$line" ]]; do
  case "$line" in
    POSTGRES_PASSWORD=) echo "POSTGRES_PASSWORD=$postgres_password" ;;
    JWT_SIGNING_KEY=)   echo "JWT_SIGNING_KEY=$jwt_key" ;;
    ADMIN_EMAIL=)       echo "ADMIN_EMAIL=admin@example.test" ;;
    ADMIN_PASSWORD=)    echo "ADMIN_PASSWORD=$admin_password" ;;
    *)                  echo "$line" ;;
  esac
done < "$example" > "$target"

echo "Created .env with random secrets. The development admin is admin@example.test; its password is in .env."
