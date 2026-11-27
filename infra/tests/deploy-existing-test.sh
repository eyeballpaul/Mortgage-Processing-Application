#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-deploy-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/curl" "$test_dir/sleep" "$test_dir/log" "$test_dir/output"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "group exists "*) echo "${TEST_GROUP_EXISTS:-true}" ;;
  "acr show "*) echo "test.azurecr.io" ;;
  "identity show "*)
    echo '{"id":"/identities/mortgage-demo-env-mortgages","clientId":"mortgage-client","principalId":"11111111-2222-3333-4444-555555555555"}' ;;
  "deployment group create "*)
    if [[ "${TEST_NOTIFICATION_DEPLOY_FAIL:-false}" == "true" ]]; then
      echo "Notification deployment failed" >&2
      exit 1
    fi
    jq -n --arg namespace "${TEST_NAMESPACE:-mortgage.servicebus.windows.net}" \
      '{fullyQualifiedNamespace:{value:$namespace},queueName:{value:"submission-notifications"},queueId:{value:"/subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.ServiceBus/namespaces/test-sb/queues/submission-notifications"}}' ;;
  "role assignment list --assignee-object-id "*) echo '[]' ;;
  "containerapp job show "*)
    if [[ "${TEST_BASELINE:-latest}" == "current" ]]; then marker="$GITHUB_SHA"; else marker=""; fi
    jq -n --arg marker "$marker" '{id:"/job",tags:{"pd-bootstrap-commit":$marker}}' ;;
  "containerapp show "*)
    name="$4"
    if [[ "$*" == *"--query properties.configuration.ingress.fqdn"* ]]; then
      echo "$name.example"
    else
      case "$name" in
        mortgage-api) image=mortgages-api ;;
        staff-web) image=mortgages-staff-web ;;
        *) image="$name" ;;
      esac
      if [[ "${TEST_BASELINE:-latest}" == "current" ]]; then marker="$GITHUB_SHA"; tag="$marker"; else marker=""; tag=latest; fi
      config="${TEST_NOTIFICATION_CONFIG:-${TEST_BASELINE:-latest}}"
      client=mortgage-client
      if [[ "${TEST_BAD_IDENTITY:-false}" == "true" ]]; then client=wrong-client; fi
      identity_id=/identities/mortgage-demo-env-mortgages
      if [[ "${TEST_IDENTITY_UNATTACHED:-false}" == "true" ]]; then identity_id=/identities/other; fi
      jq -n --arg name "$name" --arg image "test.azurecr.io/$image:$tag" --arg marker "$marker" \
        --arg config "$config" --arg client "$client" --arg identity_id "$identity_id" \
        '{id:("/" + $name), tags:{"pd-deployed-commit":$marker},
          identity:{userAssignedIdentities:{($identity_id):{}}},
          properties:{template:{containers:[{image:$image,env:(
            [{name:"AZURE_CLIENT_ID",value:$client},
             {name:"Notifications__WorkerModel",value:(if env.TEST_LEGACY_APP == "true" then "legacy" else "separate" end)}] +
            (if $config == "current" then [
              {name:"Notifications__Enabled",value:"true"},
              {name:"Notifications__ServiceBusNamespace",value:"mortgage.servicebus.windows.net"},
              {name:"Notifications__QueueName",value:"submission-notifications"}
            ] else [] end))}]}}}'
    fi ;;
  "acr build "*|"containerapp job update "*|"containerapp update "*|"tag update "*) exit 0 ;;
  "containerapp job start "*) echo execution ;;
  "containerapp job execution show "*) echo "${TEST_JOB_STATUS:-Succeeded}" ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$test_dir/curl" <<'MOCK'
#!/usr/bin/env bash
echo "curl $*" >> "$TEST_LOG"
if [[ "${TEST_CURL_FAIL:-false}" == "true" ]]; then exit 22; fi
MOCK
cat > "$test_dir/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$test_dir/az" "$test_dir/curl" "$test_dir/sleep"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log"
export AZURE_RESOURCE_GROUP=test-rg AZURE_CONTAINER_REGISTRY=test GITHUB_SHA
GITHUB_SHA="$(git rev-parse HEAD)"

TEST_BASELINE=current bash infra/deployment/deploy-existing.sh > "$test_dir/output"
if grep -q 'acr build\|containerapp update\|job start' "$TEST_LOG"; then
  echo "An unchanged deployment performed work." >&2
  exit 1
fi
grep -q '^deployment group create --name mortgage-notifications-pipeline' "$TEST_LOG"

: > "$TEST_LOG"
TEST_BASELINE=current TEST_NOTIFICATION_CONFIG=missing bash infra/deployment/deploy-existing.sh > "$test_dir/output"
[[ "$(grep -c '^containerapp update ' "$TEST_LOG")" == 1 ]]
grep -q '^containerapp update --name mortgage-api .*--set-env-vars Notifications__Enabled=true' "$TEST_LOG"
if grep -q '^acr build\|^containerapp job start' "$TEST_LOG"; then
  echo "Notification configuration alone should not rebuild images or run bootstrap." >&2
  exit 1
fi

: > "$TEST_LOG"
bash infra/deployment/deploy-existing.sh > "$test_dir/output"
[[ "$(grep -c '^acr build ' "$TEST_LOG")" == 7 ]]
[[ "$(grep -c '^containerapp update ' "$TEST_LOG")" == 6 ]]
[[ "$(grep -c '^tag update ' "$TEST_LOG")" == 7 ]]
grep -q 'curl .*https://staff-web.example/health/ready' "$TEST_LOG"
grep -q 'curl .*https://mortgages-web.example/health/dependencies' "$TEST_LOG"
grep -q '^containerapp update --name mortgage-api .*--image .* --set-env-vars Notifications__Enabled=true' "$TEST_LOG"
bootstrap_line="$(grep -n '^containerapp job start ' "$TEST_LOG" | cut -d: -f1)"
notification_line="$(grep -n '^deployment group create ' "$TEST_LOG" | cut -d: -f1)"
app_line="$(grep -n '^containerapp update --name mortgage-api ' "$TEST_LOG" | cut -d: -f1)"
(( bootstrap_line < notification_line && notification_line < app_line ))

: > "$TEST_LOG"
if TEST_LEGACY_APP=true bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "Legacy consumer should block deployment." >&2; exit 1
fi
if grep -q '^deployment group create\|^acr build\|^containerapp update' "$TEST_LOG"; then
  echo "Resources were modified despite a legacy consumer." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_JOB_STATUS=Failed bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "A failed bootstrap should fail the deployment." >&2
  exit 1
fi
if grep -q '^containerapp update\|^tag update' "$TEST_LOG"; then
  echo "App updates or success tags occurred after a failed bootstrap." >&2
  exit 1
fi
if grep -q '^deployment group create ' "$TEST_LOG"; then
  echo "Notifications were deployed after a failed bootstrap." >&2
  exit 1
fi

: > "$TEST_LOG"
if TEST_BASELINE=current TEST_NOTIFICATION_DEPLOY_FAIL=true bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "A failed notification deployment should fail the deployment." >&2
  exit 1
fi
if grep -q '^containerapp update\|^tag update' "$TEST_LOG"; then
  echo "The app was updated after notification deployment failed." >&2
  exit 1
fi

: > "$TEST_LOG"
if TEST_BASELINE=current TEST_NAMESPACE=invalid bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "Invalid notification deployment output should fail the deployment." >&2
  exit 1
fi
if grep -q '^containerapp update\|^tag update' "$TEST_LOG"; then
  echo "The app was updated with invalid notification settings." >&2
  exit 1
fi

: > "$TEST_LOG"
if TEST_BAD_IDENTITY=true bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "Unexpected Mortgage API identity should fail the deployment." >&2
  exit 1
fi
if grep -q '^deployment group create\|^acr build\|^containerapp update' "$TEST_LOG"; then
  echo "Resources were modified despite an unexpected identity." >&2
  exit 1
fi

: > "$TEST_LOG"
if TEST_IDENTITY_UNATTACHED=true bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "An unattached Mortgage API identity should fail the deployment." >&2
  exit 1
fi
if grep -q '^deployment group create\|^acr build\|^containerapp update' "$TEST_LOG"; then
  echo "Resources were modified despite an unattached identity." >&2
  exit 1
fi

: > "$TEST_LOG"
if TEST_CURL_FAIL=true bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "A failed readiness check should fail the deployment." >&2
  exit 1
fi
if grep -q '^tag update --resource-id /staff-web\|^tag update --resource-id /mortgages-web' "$TEST_LOG"; then
  echo "App success tags were recorded after a failed readiness check." >&2
  exit 1
fi

: > "$TEST_LOG"
if TEST_GROUP_EXISTS=false bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "A missing resource group should fail the deployment." >&2
  exit 1
fi
if grep -q '^acr build\|^containerapp update\|^tag update' "$TEST_LOG"; then
  echo "The deployment modified resources in a missing group." >&2
  exit 1
fi
echo "Existing deployment mock tests passed."
