#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-quickstart-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/gh" "$test_dir/curl" "$test_dir/sleep" "$test_dir/log" "$test_dir/output" "$test_dir/roles" "$test_dir/staff"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "containerapp job execution show --help") exit 0 ;;
  "account show --output none") exit 0 ;;
  "account show --query name --output tsv") echo "Test subscription" ;;
  "account show --query id --output tsv") echo "subscription-id" ;;
  "account show --query tenantId --output tsv") echo "tenant-id" ;;
  "ad signed-in-user show --query id --output tsv") echo "user-id" ;;
  "group exists --name test-rg --output tsv") echo "${TEST_GROUP_EXISTS:-true}" ;;
  "group show --name test-rg --query location --output tsv") echo uksouth ;;
  "group create --name test-rg --location uksouth --output none") exit 0 ;;
  "sql server list --resource-group test-rg --output json")
    if [[ "${TEST_EXISTING_SERVER:-false}" == "true" ]]; then
      echo '[{"name":"sql-server"}]'
    else
      echo '[]'
    fi ;;
  "ad sp show --id api-client --output json")
    echo '{"id":"api-principal","appRoles":[{"value":"MortgageCustomer","isEnabled":true,"allowedMemberTypes":["Application"],"id":"role-id"}]}' ;;
  "ad sp show --id customer-client --output none") exit 0 ;;
  "ad sp show --id github-client --query id --output tsv") echo github-principal ;;
  "identity show --name mortgage-demo-env-"*)
    if [[ "${TEST_MISSING_IDENTITY:-false}" == true ]]; then exit 1; fi
    if [[ "$*" == *"mortgage-demo-env-customer-sessions"* ]]; then
      echo '{"principalId":"customer-principal","clientId":"customer-client"}'
    else
      echo '{"principalId":"other-principal","clientId":"other-client"}'
    fi ;;
  "ad app show --id staff-client --output json") cat "$TEST_STATE/staff" ;;
  "deployment group create "*)
    for arg in "$@"; do
      if [[ "$arg" == @* ]]; then
        parameter_file="${arg#@}"
        jq -e --arg principal "${TEST_EXPECTED_PRINCIPAL:-user-id}" --arg type "${TEST_EXPECTED_TYPE:-User}" \
          '.parameters.pipelinePrincipalId.value == $principal and
          .parameters.pipelinePrincipalType.value == $type and
          .parameters.staffClientSecret.value == "staff-secret" and
          .parameters.productServiceApiKey.value == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"' \
          "$parameter_file" > /dev/null
      fi
    done
    if [[ "$*" == *"deployApps=true"* && "${TEST_JOB_STATUS:-Succeeded}" != "Succeeded" ]]; then
      echo "Apps deployed after bootstrap failure" >&2
      exit 1
    fi ;;
  *"properties.outputs.customerSessionsIdentityPrincipalId.value"*) echo customer-principal ;;
  *"properties.outputs.customerSessionsIdentityClientId.value"*) echo customer-client ;;
  *"properties.outputs.mortgagesIdentityPrincipalId.value"*) echo 11111111-2222-3333-4444-555555555555 ;;
  *"properties.outputs.notificationQueueId.value"*) echo /subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.ServiceBus/namespaces/test-sb/queues/submission-notifications ;;
  *"properties.outputs.databaseBootstrapJobName.value"*) echo bootstrap-job ;;
  *"properties.outputs.mortgagesWebUrl.value"*) echo https://customer.example ;;
  *"properties.outputs.staffWebUrl.value"*) echo https://staff.example ;;
  "rest --method get "*)
    if [[ "${TEST_AUTOMATED_ASSIGNMENT:-false}" == true ]]; then
      echo '{"value":[{"resourceId":"api-principal","appRoleId":"role-id"}]}'
    else
      echo '{"value":[]}'
    fi ;;
  "rest --method post "*) exit 0 ;;
  "rest --method patch "*)
    if [[ "${TEST_FAIL_PATCH:-false}" == true ]]; then exit 1; fi
    if [[ "${TEST_SKIP_PATCH:-false}" == true ]]; then exit 0; fi
    while [[ $# -gt 0 ]]; do
      if [[ "$1" == --body ]]; then body="$2"; break; fi
      shift
    done
    jq -e '.web.homePageUrl == "https://existing.example" and .web.implicitGrantSettings.enableIdTokenIssuance == true and (.web | has("redirectUriSettings") | not) and
      (.web.redirectUris | index("https://localhost:7241/signin-oidc") != null and index("https://staff.example/signin-oidc") != null and index("https://staff.example/signout-callback-oidc") != null)' <<< "$body" >/dev/null
    jq --argjson body "$body" '.web = ($body.web + {redirectUriSettings: [$body.web.redirectUris[] | {uri: ., index: null}]})' "$TEST_STATE/staff" > "$TEST_STATE/staff.new"
    mv "$TEST_STATE/staff.new" "$TEST_STATE/staff" ;;
  "role assignment list "*) cat "$TEST_STATE/roles" ;;
  "role assignment create "*)
    if [[ "${TEST_FAIL_ROLE:-false}" == true ]]; then exit 1; fi
    role="" scope="" condition=""
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --role) role="$2"; shift ;;
        --scope) scope="$2"; shift ;;
        --condition) condition="$2"; shift ;;
      esac
      shift
    done
    if [[ "$role" == Contributor ]]; then
      jq --arg scope "$scope" '. + [{scope:$scope,roleDefinitionName:"Contributor",roleDefinitionId:"/roles/contributor",condition:null}]' "$TEST_STATE/roles" > "$TEST_STATE/roles.new"
    else
      jq --arg scope "$scope" --arg role "$role" --arg condition "$condition" \
        '. + [{scope:$scope,roleDefinitionName:"Role Based Access Control Administrator",roleDefinitionId:("/roles/" + $role),condition:$condition,conditionVersion:"2.0"}]' "$TEST_STATE/roles" > "$TEST_STATE/roles.new"
    fi
    mv "$TEST_STATE/roles.new" "$TEST_STATE/roles" ;;
  "acr build "*) exit 0 ;;
  "containerapp job start "*) echo job-execution ;;
  "containerapp job execution show "*) echo "${TEST_JOB_STATUS:-Succeeded}" ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$test_dir/gh" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "gh $*" >> "$TEST_LOG"
case "$*" in
  "auth status --hostname github.com") exit 0 ;;
  "repo view owner/repo --json nameWithOwner --jq .nameWithOwner") echo owner/repo ;;
  "variable get AZURE_CLIENT_ID --repo owner/repo") echo github-client ;;
  "variable get AZURE_TENANT_ID --repo owner/repo") echo "${TEST_GITHUB_TENANT:-tenant-id}" ;;
  "variable set "*)
    if [[ "${TEST_FAIL_GH:-false}" == true ]]; then exit 1; fi ;;
  *) echo "Unexpected gh invocation: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$test_dir/curl" <<'MOCK'
#!/usr/bin/env bash
echo "curl $*" >> "$TEST_LOG"
MOCK
cat > "$test_dir/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$test_dir/az" "$test_dir/gh" "$test_dir/curl" "$test_dir/sleep"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log" TEST_STATE="$test_dir"

reset_state() {
  : > "$TEST_LOG"
  echo '[]' > "$TEST_STATE/roles"
  echo '{"id":"staff-object","web":{"redirectUris":["https://localhost:7241/signin-oidc"],"redirectUriSettings":[{"uri":"https://localhost:7241/signin-oidc","index":null}],"homePageUrl":"https://existing.example","implicitGrantSettings":{"enableIdTokenIssuance":true}}}' > "$TEST_STATE/staff"
}
reset_state
inputs=$'test-rg\nregistryname\nsql-server\nstorageaccount\napi-client\nstaff-client\nstaff-secret\naaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\nn\nyes\n'
printf '%s' "$inputs" | bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output"
grep -q 'deployment group create --name mortgage-foundation .*createSqlServer=true' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-bootstrap .*deployApps=false deployBootstrap=true createSqlServer=false' "$TEST_LOG"
grep -q 'containerapp job start --name bootstrap-job' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-apps .*deployApps=true deployBootstrap=true createSqlServer=false' "$TEST_LOG"
grep -q -- '--image mortgages-api:latest --file PD.Mortgages.Api/Dockerfile' "$TEST_LOG"
grep -q -- '--image mortgages-staff-web:latest --file PD.Mortgages.Staff.Web/Dockerfile' "$TEST_LOG"
grep -q 'curl .*https://customer.example/health/ready' "$TEST_LOG"
grep -q 'curl .*https://staff.example/health/ready' "$TEST_LOG"
jq -e '.web.homePageUrl == "https://existing.example" and .web.implicitGrantSettings.enableIdTokenIssuance == true and (.web.redirectUris | length == 3)' "$TEST_STATE/staff" >/dev/null
if grep -q '^gh ' "$TEST_LOG"; then echo "Manual GitHub mode called gh." >&2; exit 1; fi

reset_state
printf '%s' "$inputs" | TEST_EXISTING_SERVER=true bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output"
grep -q 'deployment group create --name mortgage-foundation .*createSqlServer=false' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-bootstrap .*createSqlServer=false' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-apps .*createSqlServer=false' "$TEST_LOG"

reset_state
if printf '%s' "$inputs" | TEST_JOB_STATUS=Failed bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a failed bootstrap to fail the quickstart." >&2
  exit 1
fi
if grep -q 'deployment group create --name mortgage-apps' "$TEST_LOG"; then
  echo "The quickstart deployed apps after the bootstrap failed." >&2
  exit 1
fi

reset_state
new_inputs=$'test-rg\nuksouth\nregistryname\nsql-server\nstorageaccount\napi-client\nstaff-client\nstaff-secret\naaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\ny\nowner/repo\nyes\n'
printf '%s' "$new_inputs" | TEST_GROUP_EXISTS=false bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output"
grep -q 'group create --name test-rg --location uksouth' "$TEST_LOG"
grep -q 'role assignment create .*--role Contributor --scope /subscriptions/subscription-id/resourceGroups/test-rg ' "$TEST_LOG"
grep -q 'role assignment create .*--role f58310d9-a9f6-439a-9e8d-f62e7b41a168 .*--condition-version 2.0' "$TEST_LOG"
jq -e 'any(.[]; .roleDefinitionName == "Role Based Access Control Administrator" and
  (.scope | endswith("/queues/submission-notifications")) and
  (.condition | contains("@Request[Microsoft.Authorization/roleAssignments:PrincipalId]") and
    contains("@Resource[Microsoft.Authorization/roleAssignments:PrincipalId]") and
    contains("11111111-2222-3333-4444-555555555555") and
    contains("69a216fc-b8fb-44d8-bc22-1f3c2cd27a39") and
    contains("4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0")))' "$TEST_STATE/roles" >/dev/null
grep -q 'gh variable set AZURE_RESOURCE_GROUP --repo owner/repo --body test-rg' "$TEST_LOG"
grep -q 'gh variable set AZURE_CONTAINER_REGISTRY --repo owner/repo --body registryname' "$TEST_LOG"
grep -q 'gh variable set AZURE_SUBSCRIPTION_ID --repo owner/repo --body subscription-id' "$TEST_LOG"

: > "$TEST_LOG"
existing_auto_inputs=$'test-rg\nregistryname\nsql-server\nstorageaccount\napi-client\nstaff-client\nstaff-secret\naaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\ny\nowner/repo\nyes\n'
printf '%s' "$existing_auto_inputs" | TEST_EXISTING_SERVER=true bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output"
if grep -Eq '^role assignment create|^rest --method patch|^group create' "$TEST_LOG"; then
  echo "Rerun repeated an existing assignment or redirect update." >&2; exit 1
fi

reset_state
cancel_inputs="${new_inputs%yes$'\n'}no"$'\n'
if printf '%s' "$cancel_inputs" | TEST_GROUP_EXISTS=false bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output" 2>&1; then
  echo "Expected cancellation to fail." >&2; exit 1
fi
if grep -Eq '^group create|^role assignment create|^rest --method (patch|post)|^gh variable set|^deployment group create' "$TEST_LOG"; then
  echo "Cancellation made changes." >&2; exit 1
fi

for failure in TEST_FAIL_ROLE TEST_FAIL_PATCH TEST_SKIP_PATCH TEST_FAIL_GH; do
  reset_state
  if printf '%s' "$existing_auto_inputs" | env "$failure=true" bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output" 2>&1; then
    echo "Expected $failure to fail the quickstart." >&2; exit 1
  fi
  if grep -q 'GitHub repository variables updated' "$test_dir/output"; then
    echo "Reported GitHub success after $failure." >&2; exit 1
  fi
  if [[ "$failure" != TEST_FAIL_GH ]] && grep -q '^gh variable set ' "$TEST_LOG"; then
    echo "Changed GitHub variables after $failure." >&2; exit 1
  fi
done

reset_state
if printf '%s' "$existing_auto_inputs" | TEST_GITHUB_TENANT=other bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output" 2>&1; then
  echo "Expected tenant mismatch to fail." >&2; exit 1
fi
if grep -q 'deployment group create' "$TEST_LOG"; then echo "Tenant mismatch deployed resources." >&2; exit 1; fi

reset_state
echo '[{"scope":"/subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.ServiceBus/namespaces/test-sb/queues/submission-notifications","roleDefinitionName":"Role Based Access Control Administrator","roleDefinitionId":"/roles/f58310d9-a9f6-439a-9e8d-f62e7b41a168","condition":"different","conditionVersion":"2.0"}]' > "$TEST_STATE/roles"
if printf '%s' "$existing_auto_inputs" | bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a conflicting RBAC condition to fail." >&2; exit 1
fi
grep -q 'existing queue-scoped RBAC administrator assignment has a different condition' "$test_dir/output"
if grep -q 'role assignment create .*--role f58310d9' "$TEST_LOG"; then
  echo "Conflicting assignment was overwritten." >&2; exit 1
fi

reset_state
jq '.web.redirectUriSettings[0].index = 1' "$TEST_STATE/staff" > "$TEST_STATE/staff.new"
mv "$TEST_STATE/staff.new" "$TEST_STATE/staff"
if printf '%s' "$inputs" | bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output" 2>&1; then
  echo "Expected indexed redirect settings to block deployment." >&2; exit 1
fi
if grep -Eq '^deployment group create|^rest --method patch' "$TEST_LOG"; then
  echo "Changed resources despite indexed redirect settings." >&2; exit 1
fi

reset_state
export AUTOMATED_DEPLOYMENT=true AZURE_CLIENT_ID=github-client AZURE_SUBSCRIPTION_ID=subscription-id \
  AZURE_RESOURCE_GROUP=test-rg AZURE_CONTAINER_REGISTRY=registryname AZURE_SQL_SERVER=sql-server \
  AZURE_STORAGE_ACCOUNT=storageaccount MORTGAGE_API_CLIENT_ID=api-client STAFF_CLIENT_ID=staff-client \
  STAFF_CLIENT_SECRET=staff-secret PRODUCTS_API_KEY=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
  ELIGIBILITY_API_KEY=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb TEST_EXPECTED_PRINCIPAL=github-principal \
  TEST_EXPECTED_TYPE=ServicePrincipal
TEST_AUTOMATED_ASSIGNMENT=true bash "$root/infra/quickstart-deploy.sh" </dev/null > "$test_dir/output"
grep -q 'deployment group create --name mortgage-apps' "$TEST_LOG"
if grep -Eq '^gh |^rest --method post|^group create' "$TEST_LOG"; then
  echo "Unattended deployment changed GitHub, group, or app-role configuration." >&2; exit 1
fi

for failure in TEST_MISSING_IDENTITY TEST_AUTOMATED_ASSIGNMENT; do
  reset_state
  if [[ "$failure" == TEST_MISSING_IDENTITY ]]; then
    if TEST_MISSING_IDENTITY=true bash "$root/infra/quickstart-deploy.sh" </dev/null > "$test_dir/output" 2>&1; then
      echo "Expected missing identity to fail." >&2; exit 1
    fi
  else
    if TEST_AUTOMATED_ASSIGNMENT=false bash "$root/infra/quickstart-deploy.sh" </dev/null > "$test_dir/output" 2>&1; then
      echo "Expected missing app role to fail." >&2; exit 1
    fi
  fi
  if grep -q '^deployment group create' "$TEST_LOG"; then
    echo "Deployed resources despite missing prerequisites." >&2; exit 1
  fi
done
echo "Quickstart mock tests passed."
