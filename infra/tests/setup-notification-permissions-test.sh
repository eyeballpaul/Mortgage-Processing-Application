#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-permission-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/assignment" "$test_dir/identities" "$test_dir/identities.new" "$test_dir/log" "$test_dir/output"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show --query id --output tsv") echo sub ;;
  "group show --name test-rg --query id --output tsv") echo /subscriptions/sub/resourceGroups/test-rg ;;
  "ad sp show --id github-client --query id --output tsv") echo 11111111-2222-3333-4444-555555555555 ;;
  "role assignment list "*)
    if [[ "${TEST_CHANGED_CONDITION:-false}" == true && "$(grep -c '^role assignment list ' "$TEST_LOG")" -ge 2 ]]; then
      jq -s '.[0].condition = "changed" | .' "$TEST_DIR/assignment"
    else
      jq -s '.' "$TEST_DIR/assignment"
    fi ;;
  "role assignment update --role-assignment "*)
    if [[ "${TEST_FAIL_UPDATE:-false}" == true ]]; then exit 1; fi
    cp "$5" "$TEST_DIR/assignment" ;;
  "identity list "*) cat "$TEST_DIR/identities" ;;
  "identity create "*)
    jq --arg name "$4" '. + [{name:$name}]' "$TEST_DIR/identities" > "$TEST_DIR/identities.new"
    mv "$TEST_DIR/identities.new" "$TEST_DIR/identities" ;;
  "identity show --name mortgage-demo-env-notifications-scaler "*) echo aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee ;;
  "identity show --name mortgage-demo-env-notifications "*) echo 99999999-8888-7777-6666-555555555555 ;;
  "identity show --name mortgage-demo-env-outbox-publisher "*) echo 88888888-7777-6666-5555-444444444444 ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$test_dir/az"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log" TEST_DIR="$test_dir" \
  AZURE_RESOURCE_GROUP=test-rg AZURE_CLIENT_ID=github-client

reset_state() {
  : > "$TEST_LOG"
  echo '[]' > "$TEST_DIR/identities"
  echo '{"id":"/role/one","principalId":"11111111-2222-3333-4444-555555555555","scope":"/subscriptions/sub/resourceGroups/test-rg","roleDefinitionId":"/roles/f58310d9-a9f6-439a-9e8d-f62e7b41a168","condition":"original-condition","conditionVersion":"2.0"}' > "$TEST_DIR/assignment"
}
reset_state
printf 'yes\n' | bash "$root/infra/setup-notification-permissions.sh" > "$test_dir/output"
jq -e '.condition | startswith("(original-condition) OR (") and
  contains("4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0") and contains("99999999-8888-7777-6666-555555555555") and
  contains("69a216fc-b8fb-44d8-bc22-1f3c2cd27a39") and contains("88888888-7777-6666-5555-444444444444") and
  contains("090c5cfd-751d-490a-894a-3ce6f1109419") and contains("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee") and
  contains("@Request[Microsoft.Authorization/roleAssignments:PrincipalId]") and
  contains("@Resource[Microsoft.Authorization/roleAssignments:PrincipalId]")' "$TEST_DIR/assignment" >/dev/null
[[ "$(grep -c '^identity create' "$TEST_LOG")" == 3 ]]
printf 'yes\n' | bash "$root/infra/setup-notification-permissions.sh" > "$test_dir/output"
[[ "$(grep -c '^role assignment update' "$TEST_LOG")" == 1 ]]
[[ "$(grep -c '^identity create' "$TEST_LOG")" == 3 ]]

reset_state
if printf 'no\n' | bash "$root/infra/setup-notification-permissions.sh" > "$test_dir/output" 2>&1; then
  echo "Expected cancellation." >&2; exit 1
fi
if grep -Eq '^identity create|^role assignment update' "$TEST_LOG"; then
  echo "Cancellation changed Azure resources." >&2; exit 1
fi

reset_state
jq '.condition = null' "$TEST_DIR/assignment" > "$TEST_DIR/identities.new"
mv "$TEST_DIR/identities.new" "$TEST_DIR/assignment"
if printf 'yes\n' | bash "$root/infra/setup-notification-permissions.sh" > "$test_dir/output" 2>&1; then
  echo "An unconditional administrator role should block setup." >&2; exit 1
fi
if grep -Eq '^identity create|^role assignment update' "$TEST_LOG"; then
  echo "Changed Azure resources despite an unconditional role." >&2; exit 1
fi

reset_state
if printf 'yes\n' | TEST_FAIL_UPDATE=true bash "$root/infra/setup-notification-permissions.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a role update failure." >&2; exit 1
fi

reset_state
if printf 'yes\n' | TEST_CHANGED_CONDITION=true bash "$root/infra/setup-notification-permissions.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a concurrent role change to stop setup." >&2; exit 1
fi
if grep -q '^role assignment update' "$TEST_LOG"; then
  echo "Overwrote a concurrently changed role assignment." >&2; exit 1
fi
echo "Notification permission setup mock tests passed."
