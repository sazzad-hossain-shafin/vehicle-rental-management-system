#!/usr/bin/env bash
# Creates .env from .env.example with freshly generated random secrets, for Docker Compose.
#
# Fills in POSTGRES_PASSWORD, POSTGRES_APP_PASSWORD (the API's restricted database role), JWT_SIGNING_KEY and the development admin (admin@example.test with a random
# password). An existing .env is never replaced unless you pass --force. The secrets are written only to .env
# (which Git ignores); they are not printed.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
example="$root/.env.example"
target="$root/.env"

random_hex() { head -c "$1" /dev/urandom | od -An -tx1 | tr -d ' \n'; }

# --upgrade: add only the settings an older .env is missing (today: the restricted runtime role), leaving every existing
# value, including secrets, exactly as it is. Prints the NAMES it added, never values.
if [[ -e "$target" && "${1:-}" == "--upgrade" ]]; then
  added=()
  if ! grep -qE '^POSTGRES_APP_USER=.+' "$target"; then
    sed -i '/^POSTGRES_APP_USER=\s*$/d' "$target"
    printf '\nPOSTGRES_APP_USER=vehiclerental_app\n' >> "$target"
    added+=(POSTGRES_APP_USER)
  fi
  if ! grep -qE '^POSTGRES_APP_PASSWORD=.+' "$target"; then
    sed -i '/^POSTGRES_APP_PASSWORD=\s*$/d' "$target"
    printf 'POSTGRES_APP_PASSWORD=%s\n' "$(random_hex 24)" >> "$target"
    added+=(POSTGRES_APP_PASSWORD)
  fi
  if [[ ${#added[@]} -eq 0 ]]; then echo ".env already has everything; nothing changed."; else echo "Added to .env: ${added[*]}"; fi
  exit 0
fi

if [[ -e "$target" && "${1:-}" != "--force" ]]; then
  echo ".env already exists, so it was left unchanged. Run with --force to replace it, or --upgrade to add only missing settings."
  exit 0
fi

postgres_password="$(random_hex 24)"
app_password="$(random_hex 24)"
jwt_key="$(head -c 48 /dev/urandom | base64 | tr -d '\n')"
admin_password="Aa1-$(random_hex 10)"

while IFS= read -r line || [[ -n "$line" ]]; do
  case "$line" in
    POSTGRES_PASSWORD=) echo "POSTGRES_PASSWORD=$postgres_password" ;;
    POSTGRES_APP_PASSWORD=) echo "POSTGRES_APP_PASSWORD=$app_password" ;;
    JWT_SIGNING_KEY=)   echo "JWT_SIGNING_KEY=$jwt_key" ;;
    ADMIN_EMAIL=)       echo "ADMIN_EMAIL=admin@example.test" ;;
    ADMIN_PASSWORD=)    echo "ADMIN_PASSWORD=$admin_password" ;;
    *)                  echo "$line" ;;
  esac
done < "$example" > "$target"

echo "Created .env with random secrets. The development admin is admin@example.test; its password is in .env."
