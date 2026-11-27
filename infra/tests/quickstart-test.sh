#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-quickstart-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/curl" "$test_dir/sleep" "$test_dir/log" "$test_dir/output"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "containerapp job execution show --help") exit 0 ;;
  "account show --output none") exit 0 ;;
  "account show --query name --output tsv") echo "Test subscription" ;;
  "account show --query id --output tsv") echo "subscription-id" ;;
  "ad signed-in-user show --query id --output tsv") echo "user-id" ;;
  "group exists --name test-rg --output tsv") echo true ;;
  "group show --name test-rg --query location --output tsv") echo uksouth ;;
  "sql server list --resource-group test-rg --output json")
    if [[ "${TEST_EXISTING_SERVER:-false}" == "true" ]]; then
      echo '[{"name":"sql-server"}]'
    else
      echo '[]'
    fi ;;
  "ad sp show --id api-client --output json")
    echo '{"id":"api-principal","appRoles":[{"value":"MortgageCustomer","isEnabled":true,"allowedMemberTypes":["Application"],"id":"role-id"}]}' ;;
  "ad sp show --id customer-client --output none") exit 0 ;;
  "deployment group create "*)
    for arg in "$@"; do
      if [[ "$arg" == @* ]]; then
        parameter_file="${arg#@}"
        jq -e '.parameters.pipelinePrincipalId.value == "user-id" and
          .parameters.pipelinePrincipalType.value == "User" and
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
  *"properties.outputs.databaseBootstrapJobName.value"*) echo bootstrap-job ;;
  *"properties.outputs.mortgagesWebUrl.value"*) echo https://customer.example ;;
  *"properties.outputs.staffWebUrl.value"*) echo https://staff.example ;;
  "rest --method get "*) echo '{"value":[]}' ;;
  "rest --method post "*) exit 0 ;;
  "acr build "*) exit 0 ;;
  "containerapp job start "*) echo job-execution ;;
  "containerapp job execution show "*) echo "${TEST_JOB_STATUS:-Succeeded}" ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
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
chmod +x "$test_dir/az" "$test_dir/curl" "$test_dir/sleep"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log"

inputs=$'test-rg\nregistryname\nsql-server\nstorageaccount\napi-client\nstaff-client\nstaff-secret\naaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\nyes\n'
printf '%s' "$inputs" | bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output"
grep -q 'deployment group create --name mortgage-foundation .*createSqlServer=true' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-bootstrap .*deployApps=false deployBootstrap=true createSqlServer=false' "$TEST_LOG"
grep -q 'containerapp job start --name bootstrap-job' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-apps .*deployApps=true deployBootstrap=true createSqlServer=false' "$TEST_LOG"
grep -q -- '--image mortgages-api:latest --file PD.Mortgages.Api/Dockerfile' "$TEST_LOG"
grep -q -- '--image mortgages-staff-web:latest --file PD.Mortgages.Staff.Web/Dockerfile' "$TEST_LOG"
grep -q 'curl .*https://customer.example/health/ready' "$TEST_LOG"
grep -q 'curl .*https://staff.example/health/ready' "$TEST_LOG"

: > "$TEST_LOG"
printf '%s' "$inputs" | TEST_EXISTING_SERVER=true bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output"
grep -q 'deployment group create --name mortgage-foundation .*createSqlServer=false' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-bootstrap .*createSqlServer=false' "$TEST_LOG"
grep -q 'deployment group create --name mortgage-apps .*createSqlServer=false' "$TEST_LOG"

: > "$TEST_LOG"
if printf '%s' "$inputs" | TEST_JOB_STATUS=Failed bash "$root/infra/quickstart-deploy.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a failed bootstrap to fail the quickstart." >&2
  exit 1
fi
if grep -q 'deployment group create --name mortgage-apps' "$TEST_LOG"; then
  echo "The quickstart deployed apps after the bootstrap failed." >&2
  exit 1
fi
echo "Quickstart mock tests passed."
