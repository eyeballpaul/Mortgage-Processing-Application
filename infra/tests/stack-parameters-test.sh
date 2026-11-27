#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-parameters-test.XXXXXXXX")"
trap 'rm -f -- "$dir/az" "$dir/log" "$dir/output" "$dir/parameters.json"; rmdir "$dir"' EXIT

cat > "$dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show "*) echo sub ;;
  "group show "*) echo '{"id":"/subscriptions/sub/resourceGroups/rg","location":"uksouth"}' ;;
  "ad sp show "*) echo 11111111-1111-1111-1111-111111111111 ;;
  "sql server list "*)
    if [[ "${TEST_SQL_MISSING:-false}" == true ]]; then echo '[]'; else echo '[{"name":"sql"}]'; fi ;;
  "acr show "*) echo "${TEST_REGISTRY-test.azurecr.io}" ;;
  "identity list "*)
    if [[ "${TEST_BAD_IDENTITY:-false}" == true ]]; then
      echo '[{"name":"mortgage-demo-env-pull","id":"/outside","clientId":"client"}]'
    elif [[ "${TEST_BAD_IDENTITY:-false}" == mismatch ]]; then
      echo '[{"name":"mortgage-demo-env-pull","id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/other","clientId":"client"}]'
    else
      jq -cn '["pull","mortgages","products","customer-sessions","bootstrap","staff","notifications","notifications-scaler"] |
        map({name:("mortgage-demo-env-" + .),id:("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mortgage-demo-env-" + .),clientId:"client"})'
    fi ;;
  "sql server show "*) echo '{"fullyQualifiedDomainName":"sql.database.windows.net"}' ;;
  "servicebus namespace list "*) echo '[{"name":"mortgage-sb-123456789abcd","serviceBusEndpoint":"https://sb.servicebus.windows.net:443/"}]' ;;
  "containerapp list "*) echo '[{"name":"eligibility-api"}]' ;;
  "containerapp job list "*) echo '[{"name":"mortgage-demo-env-bootstrap"}]' ;;
  "containerapp job show "*) echo '{"tags":{"application":"pd-mortgages-demo","managedBy":"bicep"}}' ;;
  "containerapp show "*)
    if [[ "$*" == *"--query properties.configuration.ingress.fqdn"* ]]; then echo app.internal.example
    else echo '{"tags":{"application":"pd-mortgages-demo","managedBy":"bicep"}}'; fi ;;
  "bicep build "*)
    if [[ -n "${TEST_REAL_TEMPLATES:-}" ]]; then
      file="$4"
      name="${file##*/}"
      name="${name%.bicep}-deployment"
      if [[ "$name" == foundation-deployment ]]; then name=mortgage-foundation-deployment; fi
      jq -er --arg name "$name" '.template | fromjson | .resources[] |
        select(.name == $name) | .properties.template' "$TEST_REAL_TEMPLATES"
    elif [[ "$*" == *"infra/foundation.bicep"* ]]; then
      echo '{"parameters":{"location":{"type":"string"},"containerRegistryName":{"type":"string"},"containerAppEnvironmentName":{"type":"string"},"sqlServerName":{"type":"string"},"documentStorageAccountName":{"type":"string"},"pipelinePrincipalId":{"type":"string"},"pipelinePrincipalType":{"type":"string"},"createSqlServer":{"type":"bool"},"deployAlerts":{"type":"bool"},"alertEmail":{"type":"string","defaultValue":""}}}'
    else
      echo '{"parameters":{"name":{"type":"string"},"location":{"type":"string"},"environmentId":{"type":"string"},"pullIdentityId":{"type":"string"},"registryServer":{"type":"string"},"imageTag":{"type":"string"},"imageRef":{"type":"string","defaultValue":""},"tags":{"type":"object"},"otlpEndpoint":{"type":"string"},"otlpProtocol":{"type":"string"},"eligibilityServiceApiKey":{"type":"secureString"}}}'
    fi ;;
  *) echo "Unexpected Azure command: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$dir/az"
export PATH="$dir:$PATH" TEST_LOG="$dir/log"
export AZURE_SUBSCRIPTION_ID=sub AZURE_RESOURCE_GROUP=rg AZURE_CONTAINER_REGISTRY=test
export AZURE_SQL_SERVER=sql AZURE_STORAGE_ACCOUNT=storage AZURE_CLIENT_ID=client
export MORTGAGE_API_CLIENT_ID=api STAFF_CLIENT_ID=staff STAFF_CLIENT_SECRET=staff-secret
export PRODUCTS_API_KEY=products-secret ELIGIBILITY_API_KEY=eligibility-secret
export STACK_PARAMETERS="$dir/parameters.json"

STACK_TARGET=foundation bash infra/deployment/stack-parameters.sh > "$dir/output"
jq -e '.parameters.createSqlServer.value == false and .parameters.deployAlerts.value == true and
  .parameters.pipelinePrincipalId.value == "11111111-1111-1111-1111-111111111111"' "$STACK_PARAMETERS" >/dev/null
TEST_SQL_MISSING=true STACK_TARGET=foundation bash infra/deployment/stack-parameters.sh > "$dir/output"
jq -e '.parameters.createSqlServer.value == true' "$STACK_PARAMETERS" >/dev/null

chmod 644 "$STACK_PARAMETERS"
OTLP_PROTOCOL='' STACK_TARGET=eligibility-api STACK_IMAGE=test.azurecr.io/eligibility-api:latest \
  bash infra/deployment/stack-parameters.sh > "$dir/output"
python3 -c 'import os, stat; assert stat.S_IMODE(os.stat(os.environ["STACK_PARAMETERS"]).st_mode) == 0o600'
jq -e '.parameters.name.value == "eligibility-api" and .parameters.otlpEndpoint.value == "" and
  .parameters.otlpProtocol.value == "grpc" and
  .parameters.eligibilityServiceApiKey.value == "eligibility-secret" and
  (.parameters.pullIdentityId.value | endswith("/mortgage-demo-env-pull"))' "$STACK_PARAMETERS" >/dev/null
if grep -q 'staff-secret\|products-secret\|eligibility-secret' "$TEST_LOG" "$dir/output"; then
  echo "Secret values appeared in command logs." >&2; exit 1
fi

for bad_identity in true mismatch; do
  if TEST_BAD_IDENTITY="$bad_identity" STACK_TARGET=eligibility-api STACK_IMAGE=test.azurecr.io/eligibility-api:latest \
    bash infra/deployment/stack-parameters.sh > "$dir/output" 2>&1; then
    echo "An invalid identity was accepted." >&2; exit 1
  fi
done
if TEST_REGISTRY='' STACK_TARGET=eligibility-api STACK_IMAGE=test.azurecr.io/eligibility-api:latest \
  bash infra/deployment/stack-parameters.sh > "$dir/output" 2>&1; then
  echo "An empty required registry was accepted." >&2; exit 1
fi
jq -e '.parameters.registryServer.value == "test.azurecr.io"' "$STACK_PARAMETERS" >/dev/null
if [[ -n "${TEST_REAL_TEMPLATES:-}" ]]; then
  for target in foundation database-bootstrap products-api eligibility-api mortgage-api notifications-worker mortgages-web staff-web; do
    STACK_TARGET="$target" STACK_IMAGE=test.azurecr.io/example:latest \
      bash infra/deployment/stack-parameters.sh > "$dir/output"
    jq -e '.parameters | type == "object"' "$STACK_PARAMETERS" >/dev/null
  done
fi
echo "Stack parameter tests passed."
