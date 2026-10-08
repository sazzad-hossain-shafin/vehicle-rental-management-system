#!/usr/bin/env bash
# End-to-end check of a RUNNING API (for example the Docker Compose stack) over real HTTP.
#
#   scripts/smoke-test.sh                  # reads API_PORT and the admin credentials from .env
#   BASE_URL=http://localhost:8080 ADMIN_EMAIL=... ADMIN_PASSWORD=... scripts/smoke-test.sh
#
# It signs in as the configured admin, creates a staff account, two customers, two vehicles and two rentals
# with unique names (so it can be run repeatedly), and checks health, public access, 401/403, customer
# ownership and the reservation flow (availability, booking, overlap, cancellation, pickup). It prints only pass/fail lines: never passwords or tokens. Exit status is non-zero on any failure.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -f "$root/.env" ]]; then
  set -a; source <(sed 's/\r$//' "$root/.env"); set +a
fi

base="${BASE_URL:-http://localhost:${API_PORT:-8080}}"
admin_email="${ADMIN_EMAIL:?ADMIN_EMAIL is required (set it in .env or the environment)}"
admin_password="${ADMIN_PASSWORD:?ADMIN_PASSWORD is required (set it in .env or the environment)}"

run="$(( RANDOM * 1000 + RANDOM % 1000 ))"   # up to 8 digits, so registration numbers stay within 12 characters
json='Content-Type: application/json'
failures=0

check() { # check "description" expected actual
  if [[ "$2" == "$3" ]]; then echo "  ok    $1"; else echo "  FAIL  $1 (expected $2, got $3)"; failures=$((failures+1)); fi
}
status() { curl -s -m 20 -o /dev/null -w '%{http_code}' "$@"; }
get() { curl -s -m 20 "$@"; }
field() { sed -n "s/.*\"$1\":\"\\([^\"]*\\)\".*/\\1/p" | head -1; }
number() { sed -n "s/.*\"$1\":\\([0-9]*\\).*/\\1/p" | head -1; }
login() { get -X POST "$base/api/v1/auth/login" -H "$json" -d "{\"email\":\"$1\",\"password\":\"$2\"}" | field accessToken; }

echo "Smoke test against $base"
echo "Health"
check "/health/live is healthy" 200 "$(status "$base/health/live")"
check "/health is healthy (database reachable and migrated)" 200 "$(status "$base/health")"

echo "Public and protected access"
check "anonymous vehicle browsing" 200 "$(status "$base/api/v1/vehicles")"
check "protected endpoint without a token" 401 "$(status "$base/api/v1/rentals")"
check "garbage token" 401 "$(status "$base/api/v1/rentals" -H 'Authorization: Bearer garbage')"

echo "Admin"
admin="$(login "$admin_email" "$admin_password")"
check "admin login returns a token" yes "$([[ -n "$admin" ]] && echo yes || echo no)"
check "wrong password is refused" 401 "$(status -X POST "$base/api/v1/auth/login" -H "$json" -d "{\"email\":\"$admin_email\",\"password\":\"Wrong-pass-1\"}")"
staff_email="staff-$run@example.test"; staff_password="Aa1-Staff$run"
check "admin creates a staff account" 201 "$(status -X POST "$base/api/v1/admin/staff" -H "$json" -H "Authorization: Bearer $admin" -d "{\"email\":\"$staff_email\",\"password\":\"$staff_password\"}")"
staff="$(login "$staff_email" "$staff_password")"
check "staff can call a protected endpoint" 200 "$(status "$base/api/v1/rentals" -H "Authorization: Bearer $staff")"
check "staff cannot create staff (admin only)" 403 "$(status -X POST "$base/api/v1/admin/staff" -H "$json" -H "Authorization: Bearer $staff" -d "{\"email\":\"x-$run@example.test\",\"password\":\"Aa1-Other$run\"}")"

echo "Customers and ownership"
register() { get -X POST "$base/api/v1/auth/register" -H "$json" -d "{\"email\":\"$1\",\"password\":\"$2\",\"name\":\"$3\",\"role\":\"Admin\"}"; }
alice_email="alice-$run@example.test"; alice_password="Aa1-Alice$run"
bob_email="bob-$run@example.test"; bob_password="Aa1-Bobby$run"
alice_user="$(register "$alice_email" "$alice_password" "Alice $run")"
bob_user="$(register "$bob_email" "$bob_password" "Bob $run")"
check "registration ignores a role in the body" '"Customer"' "$(echo "$alice_user" | sed -n 's/.*"roles":\[\([^]]*\)\].*/\1/p')"
alice_customer="$(echo "$alice_user" | field customerId)"; bob_customer="$(echo "$bob_user" | field customerId)"
alice="$(login "$alice_email" "$alice_password")"
check "customer cannot use a staff endpoint" 403 "$(status "$base/api/v1/rentals" -H "Authorization: Bearer $alice")"

echo "Rental flow"
vehicle_a="$(get -X POST "$base/api/v1/vehicles" -H "$json" -H "Authorization: Bearer $staff" -d "{\"registrationNumber\":\"A-$run\",\"make\":\"Toyota\",\"model\":\"Corolla\",\"year\":2022,\"vehicleType\":\"Car\",\"dailyRate\":60}" | field id)"
vehicle_b="$(get -X POST "$base/api/v1/vehicles" -H "$json" -H "Authorization: Bearer $staff" -d "{\"registrationNumber\":\"B-$run\",\"make\":\"Honda\",\"model\":\"CB500\",\"year\":2021,\"vehicleType\":\"Motorcycle\",\"dailyRate\":40}" | field id)"
rental_a_json="$(get -X POST "$base/api/v1/rentals" -H "$json" -H "Authorization: Bearer $staff" -d "{\"vehicleId\":\"$vehicle_a\",\"customerId\":\"$alice_customer\",\"rentalDays\":3}")"
rental_a="$(echo "$rental_a_json" | field id)"
rental_b="$(get -X POST "$base/api/v1/rentals" -H "$json" -H "Authorization: Bearer $staff" -d "{\"vehicleId\":\"$vehicle_b\",\"customerId\":\"$bob_customer\",\"rentalDays\":5}" | field id)"
check "staff starts a rental (3 days at 60 = 180.00)" 180.00 "$(echo "$rental_a_json" | sed -n 's/.*"totalCost":\([0-9.]*\).*/\1/p')"
check "renting a rented vehicle is a conflict" 409 "$(status -X POST "$base/api/v1/rentals" -H "$json" -H "Authorization: Bearer $staff" -d "{\"vehicleId\":\"$vehicle_a\",\"customerId\":\"$alice_customer\",\"rentalDays\":2}")"
check "customer reads their own rental" 200 "$(status "$base/api/v1/rentals/$rental_a" -H "Authorization: Bearer $alice")"
check "customer reading another's rental looks like a missing one" 404 "$(status "$base/api/v1/rentals/$rental_b" -H "Authorization: Bearer $alice")"
check "customer reading another's profile is refused" 403 "$(status "$base/api/v1/customers/$bob_customer" -H "Authorization: Bearer $alice")"
check "customer's rental list holds only their own" 1 "$(get "$base/api/v1/me/rentals?customerId=$bob_customer" -H "Authorization: Bearer $alice" | number totalCount)"
check "staff returns a rental" 200 "$(status -X POST "$base/api/v1/rentals/$rental_a/return" -H "Authorization: Bearer $staff")"
check "returning twice is a conflict" 409 "$(status -X POST "$base/api/v1/rentals/$rental_a/return" -H "Authorization: Bearer $staff")"
check "invalid request is a 400" 400 "$(status -X POST "$base/api/v1/vehicles" -H "$json" -H "Authorization: Bearer $staff" -d '{}')"

echo "Reservation flow"
# Dates are relative to the API's own "today", which the rental above reported, so a different time zone cannot matter.
today="$(echo "$rental_a_json" | field startDate)"
add_days() { date -u -d "$1 + $2 days" +%F 2>/dev/null || date -u -j -v+"$2"d -f %F "$1" +%F; }
d3="$(add_days "$today" 3)"; d6="$(add_days "$today" 6)"; d8="$(add_days "$today" 8)"; d2="$(add_days "$today" 2)"
vehicle_c="$(get -X POST "$base/api/v1/vehicles" -H "$json" -H "Authorization: Bearer $staff" -d "{\"registrationNumber\":\"C-$run\",\"make\":\"Mazda\",\"model\":\"3\",\"year\":2023,\"vehicleType\":\"Car\",\"dailyRate\":50}" | field id)"
bob="$(login "$bob_email" "$bob_password")"
check "availability needs a sign-in" 401 "$(status "$base/api/v1/vehicles/availability?startDate=$d3&endDate=$d6")"
check "a customer finds the free vehicle for the dates" 1 "$(get "$base/api/v1/vehicles/availability?startDate=$d3&endDate=$d6" -H "Authorization: Bearer $alice" | grep -c "$vehicle_c")"
check "availability rejects an end date before the start" 400 "$(status "$base/api/v1/vehicles/availability?startDate=$d6&endDate=$d3" -H "Authorization: Bearer $alice")"
reservation_json="$(get -X POST "$base/api/v1/me/reservations" -H "$json" -H "Authorization: Bearer $alice" -d "{\"vehicleId\":\"$vehicle_c\",\"startDate\":\"$d3\",\"endDate\":\"$d6\",\"customerId\":\"$bob_customer\"}")"
reservation="$(echo "$reservation_json" | field id)"
check "a customer reserves for themselves (3 days at 50 = 150.00)" 150.00 "$(echo "$reservation_json" | sed -n 's/.*"totalCost":\([0-9.]*\).*/\1/p')"
check "the booking belongs to the signed-in customer, not the one in the body" "$alice_customer" "$(echo "$reservation_json" | field customerId)"
check "an overlapping reservation is a conflict" 409 "$(status -X POST "$base/api/v1/me/reservations" -H "$json" -H "Authorization: Bearer $bob" -d "{\"vehicleId\":\"$vehicle_c\",\"startDate\":\"$d2\",\"endDate\":\"$d6\"}")"
check "a reservation that starts when another ends is allowed" 201 "$(status -X POST "$base/api/v1/me/reservations" -H "$json" -H "Authorization: Bearer $bob" -d "{\"vehicleId\":\"$vehicle_c\",\"startDate\":\"$d6\",\"endDate\":\"$d8\"}")"
check "another customer's reservation looks like a missing one" 404 "$(status "$base/api/v1/me/reservations/$reservation" -H "Authorization: Bearer $bob")"
check "a customer cannot list all reservations" 403 "$(status "$base/api/v1/reservations" -H "Authorization: Bearer $alice")"
check "a customer sees only their own reservations" 1 "$(get "$base/api/v1/me/reservations" -H "Authorization: Bearer $alice" | number totalCount)"
check "a customer cancels their own reservation" 200 "$(status -X POST "$base/api/v1/me/reservations/$reservation/cancel" -H "Authorization: Bearer $alice")"
check "cancelling twice is a conflict" 409 "$(status -X POST "$base/api/v1/me/reservations/$reservation/cancel" -H "Authorization: Bearer $alice")"
check "a cancelled reservation frees its dates" 201 "$(status -X POST "$base/api/v1/me/reservations" -H "$json" -H "Authorization: Bearer $bob" -d "{\"vehicleId\":\"$vehicle_c\",\"startDate\":\"$d3\",\"endDate\":\"$d6\"}")"
desk_json="$(get -X POST "$base/api/v1/reservations" -H "$json" -H "Authorization: Bearer $staff" -d "{\"customerId\":\"$alice_customer\",\"vehicleId\":\"$vehicle_a\",\"startDate\":\"$today\",\"endDate\":\"$d2\"}")"
desk_reservation="$(echo "$desk_json" | field id)"
check "staff books at the desk for a customer" Active "$(get "$base/api/v1/reservations/$desk_reservation" -H "Authorization: Bearer $staff" | field status)"
check "a customer cannot pick up a reservation" 403 "$(status -X POST "$base/api/v1/reservations/$desk_reservation/pickup" -H "Authorization: Bearer $alice")"
check "staff pick up the reservation (starts the rental)" 200 "$(status -X POST "$base/api/v1/reservations/$desk_reservation/pickup" -H "Authorization: Bearer $staff")"
check "picking up twice is a conflict" 409 "$(status -X POST "$base/api/v1/reservations/$desk_reservation/pickup" -H "Authorization: Bearer $staff")"

echo
if [[ $failures -eq 0 ]]; then echo "All checks passed."; else echo "$failures check(s) FAILED."; fi
exit $((failures > 0))
