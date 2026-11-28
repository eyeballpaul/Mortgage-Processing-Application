#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-bootstrap-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/gh" "$test_dir/uuidgen" "$test_dir/log" "$test_dir/output"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show --output none") exit 0 ;;
  "account show --query id --output tsv") echo subscription-id ;;
  "account show --query tenantId --output tsv") echo tenant-id ;;
  "ad signed-in-user show --query userPrincipalName --output tsv") echo admin@example.test ;;
  "group exists --name prototype-rg --output tsv") echo false ;;
  "ad user show --id staff@example.test --query id --output tsv") echo 11111111-2222-3333-4444-555555555555 ;;
  "ad app list --filter displayName eq "*)
    if [[ "${TEST_FAIL_GROUP_CREATE:-false}" == true ]]; then
      echo '[]'
    else
      echo '[{"displayName":"Mortgage API"}]'
    fi ;;
  "group create --name prototype-rg --location uksouth --output none")
    if [[ "${TEST_FAIL_GROUP_CREATE:-false}" == true ]]; then exit 1; fi ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$test_dir/gh" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "auth status --hostname github.com") exit 0 ;;
  "repo view owner/repo --json nameWithOwner --jq .nameWithOwner") echo owner/repo ;;
  "api repos/owner/repo --jq "*) echo '{"owner":"owner","owner_id":123,"name":"repo","id":456}' ;;
  *) echo "Unexpected gh invocation: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$test_dir/uuidgen" <<'MOCK'
#!/usr/bin/env bash
echo 11111111-2222-3333-4444-555555555555
MOCK
chmod +x "$test_dir/az" "$test_dir/gh" "$test_dir/uuidgen"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log"

inputs=$'owner/repo\nstaff@example.test\nprototype-rg\n'
if printf '%s' "$inputs" | bash "$root/infra/bootstrap-prototype.sh" > "$test_dir/output" 2>&1; then
  echo "Expected existing app registration to stop fresh bootstrap." >&2
  exit 1
fi
grep -q "will not adopt or overwrite it" "$test_dir/output"
if grep -Eq '^group create|^ad app create|^ad sp create' "$TEST_LOG"; then
  echo "Fresh bootstrap changed Azure before refusing the existing app." >&2
  exit 1
fi

: > "$TEST_LOG"
inputs=$'owner/repo\nstaff@example.test\nprototype-rg\n\n\n\n\nyes\n'
if printf '%s' "$inputs" | TEST_FAIL_GROUP_CREATE=true \
  bash "$root/infra/bootstrap-prototype.sh" > "$test_dir/output" 2>&1; then
  echo "Expected the mocked resource-group creation to fail." >&2
  exit 1
fi
grep -q "Bootstrap stopped while creating the resource group and Entra applications" "$test_dir/output"
echo "Fresh bootstrap preflight tests passed."
