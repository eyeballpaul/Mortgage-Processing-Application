#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-group-permissions-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/log" "$test_dir/assignments" "$test_dir/output"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show --query id --output tsv") echo subscription-id ;;
  "account show --query tenantId --output tsv") echo tenant-id ;;
  "group show --name test-rg --query id --output tsv") echo /subscriptions/subscription-id/resourceGroups/test-rg ;;
  "ad sp show --id github-client --query id --output tsv") echo 11111111-2222-3333-4444-555555555555 ;;
  "identity show --name mortgage-demo-env-pull --resource-group test-rg --query principalId --output tsv") echo 22222222-2222-3333-4444-555555555555 ;;
  "identity show --name mortgage-demo-env-mortgages --resource-group test-rg --query principalId --output tsv") echo 33333333-2222-3333-4444-555555555555 ;;
  "identity show --name mortgage-demo-env-customer-sessions --resource-group test-rg --query principalId --output tsv") echo 44444444-2222-3333-4444-555555555555 ;;
  "identity show --name mortgage-demo-env-staff --resource-group test-rg --query principalId --output tsv") echo 55555555-2222-3333-4444-555555555555 ;;
  "role definition list --name "*" --output json")
    role="${5}"
    case "$role" in
      "AcrPush") id=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa ;;
      "AcrPull") id=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb ;;
      "Storage Blob Data Contributor") id=cccccccc-cccc-cccc-cccc-cccccccccccc ;;
      "Azure Service Bus Data Sender") id=dddddddd-dddd-dddd-dddd-dddddddddddd ;;
      "Azure Service Bus Data Receiver") id=eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee ;;
      *) echo "Unexpected role $role" >&2; exit 1 ;;
    esac
    jq -n --arg name "$role" --arg id "$id" '[{roleName:$name,name:$id}]' ;;
  "role assignment list --assignee-object-id "*" --scope "*" --output json")
    cat "$TEST_DIR/assignments" ;;
  "role assignment create "*)
    if [[ "${TEST_FAIL_CREATE:-false}" == true ]]; then exit 1; fi
    condition="" role="" scope=""
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --condition) condition="$2"; shift ;;
        --role) role="$2"; shift ;;
        --scope) scope="$2"; shift ;;
      esac
      shift
    done
    jq --arg condition "$condition" --arg scope "$scope" --arg role "$role" \
      '. + [{scope:$scope,roleDefinitionName:"Role Based Access Control Administrator",roleDefinitionId:("/roles/" + $role),condition:$condition,conditionVersion:"2.0"}]' \
      "$TEST_DIR/assignments" > "$TEST_DIR/assignments.new"
    mv "$TEST_DIR/assignments.new" "$TEST_DIR/assignments" ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$test_dir/az"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log" TEST_DIR="$test_dir" \
  AZURE_RESOURCE_GROUP=test-rg AZURE_CLIENT_ID=github-client

reset_state() {
  : > "$TEST_LOG"
  cat > "$TEST_DIR/assignments" <<'JSON'
[{"scope":"/subscriptions/subscription-id/resourceGroups/test-rg","roleDefinitionName":"Contributor","roleDefinitionId":"/roles/contributor","condition":null}]
JSON
}

reset_state
printf 'yes\n' | bash "$root/infra/setup-deployment-permissions.sh" > "$test_dir/output"
jq -e 'any(.[]; .roleDefinitionName == "Role Based Access Control Administrator" and
  .conditionVersion == "2.0" and
  (.condition | contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") and
    contains("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee") and
    contains("11111111-2222-3333-4444-555555555555") and
    contains("55555555-2222-3333-4444-555555555555") and
    contains("@Request[Microsoft.Authorization/roleAssignments:PrincipalId]") and
    contains("@Resource[Microsoft.Authorization/roleAssignments:PrincipalId]")))' "$TEST_DIR/assignments" >/dev/null
[[ "$(grep -c '^role assignment create ' "$TEST_LOG")" == 1 ]]

: > "$TEST_LOG"
printf 'yes\n' | bash "$root/infra/setup-deployment-permissions.sh" > "$test_dir/output"
[[ "$(grep -c '^role assignment create ' "$TEST_LOG" || true)" == 0 ]]

reset_state
jq '. + [{scope:"/subscriptions/subscription-id/resourceGroups/test-rg",roleDefinitionName:"Role Based Access Control Administrator",roleDefinitionId:"/roles/f58310d9-a9f6-439a-9e8d-f62e7b41a168",condition:"different",conditionVersion:"2.0"}]' \
  "$TEST_DIR/assignments" > "$TEST_DIR/assignments.new"
mv "$TEST_DIR/assignments.new" "$TEST_DIR/assignments"
if printf 'yes\n' | bash "$root/infra/setup-deployment-permissions.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a conflicting RBAC condition to fail." >&2
  exit 1
fi
if grep -q '^role assignment create ' "$TEST_LOG"; then
  echo "Overwrote a conflicting role assignment." >&2
  exit 1
fi

reset_state
if printf 'no\n' | bash "$root/infra/setup-deployment-permissions.sh" > "$test_dir/output" 2>&1; then
  echo "Expected cancellation." >&2
  exit 1
fi
if grep -q '^role assignment create ' "$TEST_LOG"; then
  echo "Created a role assignment after cancellation." >&2
  exit 1
fi
echo "Group-scoped deployment permission tests passed."
