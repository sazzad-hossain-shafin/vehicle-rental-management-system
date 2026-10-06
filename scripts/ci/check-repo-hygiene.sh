#!/usr/bin/env bash
# Repository hygiene checks that need no tools beyond git and grep. Run by CI and runnable locally:
#
#   bash scripts/ci/check-repo-hygiene.sh
#
# They guard against the commonest ways a public repository leaks something:
#   * environment, key, certificate and local-settings files being tracked
#   * private-key blocks or JWT-shaped tokens in tracked files
#   * the example environment file or the workflows carrying real values
#   * workflow settings that are risky on a public repository
#
# These checks are generic on purpose. Anything specific to one person (names, identifiers) is checked by hand
# before publishing, because putting those values into a script in this repository would publish them.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"
failures=0
fail() { echo "FAIL: $*"; failures=$((failures + 1)); }
pass() { echo "ok:   $*"; }

# 1. Files that must never be committed
forbidden="$(git ls-files | grep -E '(^|/)(\.env|\.env\.[^/]*|secrets\.json|appsettings\.Local\.json|.*\.pfx|.*\.pem|.*\.key|.*\.p12|id_rsa|id_ed25519)$' | grep -v -E '(^|/)\.env\.example$' || true)"
if [[ -n "$forbidden" ]]; then fail "secret-bearing files are tracked:"; echo "$forbidden" | sed 's/^/        /'; else pass "no .env, key, certificate or local-settings files are tracked"; fi

# 2. Build output and test results must not be tracked
artifacts="$(git ls-files | grep -E '(^|/)(bin|obj|TestResults)/' || true)"
if [[ -n "$artifacts" ]]; then fail "build or test output is tracked:"; echo "$artifacts" | sed 's/^/        /' | head; else pass "no build or test output is tracked"; fi

# 3. .env is ignored
if git check-ignore -q .env; then pass ".env is git-ignored"; else fail ".env is not git-ignored"; fi

# 4. Private keys / tokens inside tracked files
keys="$(git grep -I -l -E -e '-----BEGIN [A-Z ]*PRIVATE KEY-----' -- . ':!scripts/ci/check-repo-hygiene.sh' || true)"
if [[ -n "$keys" ]]; then fail "a private key block is present in: $keys"; else pass "no private-key blocks in tracked files"; fi

jwts="$(git grep -I -l -E -e 'eyJ[A-Za-z0-9_-]{15,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}' -- . ':!scripts/ci/check-repo-hygiene.sh' || true)"
if [[ -n "$jwts" ]]; then fail "a complete JWT-shaped token is present in: $jwts"; else pass "no complete JWT-shaped tokens in tracked files"; fi

# 5. The example environment file must not carry real secret values
if [[ -f .env.example ]]; then
  bad=""
  for key in POSTGRES_PASSWORD JWT_SIGNING_KEY ADMIN_PASSWORD; do
    value="$(grep -E "^${key}=" .env.example | head -1 | cut -d= -f2-)"
    [[ -n "$value" ]] && bad="$bad $key"
  done
  if [[ -n "$bad" ]]; then fail ".env.example has values for:$bad (secrets must stay empty)"; else pass ".env.example holds no secret values"; fi
else
  fail ".env.example is missing"
fi

# 6. Workflows: nothing risky on a public repository
if compgen -G ".github/workflows/*.yml" > /dev/null; then
  if grep -R -n -E '^\s*pull_request_target\s*:|^\s*-\s*pull_request_target\s*$|\[.*pull_request_target.*\]' .github/workflows > /dev/null; then
    fail "a workflow uses pull_request_target, which can expose secrets to code from forks"
  else
    pass "no workflow uses pull_request_target"
  fi

  unpinned="$(grep -R -n -E '^\s*-?\s*uses:\s*[^./][^@ ]*@' .github/workflows | grep -v -E '@[0-9a-f]{40}\b' || true)"
  if [[ -n "$unpinned" ]]; then fail "actions are not pinned to a full commit SHA:"; echo "$unpinned" | sed 's/^/        /'; else pass "every action is pinned to a full commit SHA"; fi

  if grep -R -n -E '\$\{\{\s*secrets\.' .github/workflows | grep -v 'secrets.GITHUB_TOKEN' > /dev/null; then
    fail "a workflow uses repository secrets (CI is expected to need none)"
  else
    pass "workflows use no repository secrets"
  fi

  if grep -R -n -E 'contents:\s*write|packages:\s*write|id-token:\s*write' .github/workflows > /dev/null; then
    fail "a workflow requests a write permission (CI must be read-only)"
  else
    pass "no workflow requests write permissions"
  fi
else
  fail "no workflow files found under .github/workflows"
fi

echo
if [[ $failures -eq 0 ]]; then echo "Repository hygiene: all checks passed."; else echo "Repository hygiene: $failures check(s) FAILED."; fi
exit $((failures > 0))
