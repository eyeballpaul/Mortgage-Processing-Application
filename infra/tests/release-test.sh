#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-release-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/gh" "$test_dir/curl" "$test_dir/sleep" "$test_dir/log" "$test_dir/output" "$test_dir/manifest.json" "$test_dir/infra-preview.json"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show "*) echo sub ;;
  "group exists "*) echo "${TEST_GROUP_EXISTS:-true}" ;;
  "resource list "*) if [[ -n "${TEST_REGISTRIES:-}" ]]; then echo "$TEST_REGISTRIES"; else echo '[{"name":"test"}]'; fi ;;
  "acr build "*) : ;;
  "acr repository show "*)
    if [[ "$*" == *"--query digest"* ]]; then echo "$TEST_DIGEST"; fi ;;
  "acr show "*) echo test.azurecr.io ;;
  "containerapp show "*)
    if [[ "$*" == *"--query properties.configuration.ingress.fqdn"* ]]; then
      echo app.example
    else
      jq -n --arg worker "${TEST_WORKER_MODEL:-separate}" \
        '{id:"/app",tags:{},identity:{userAssignedIdentities:{"/identity":{}}},properties:{template:{containers:[{env:[{name:"AZURE_CLIENT_ID",value:"client"},{name:"Notifications__WorkerModel",value:$worker}]}]}}}'
    fi ;;
  "identity show "*) echo '{"id":"/identity","clientId":"client"}' ;;
  "containerapp job show "*) echo /job ;;
  "containerapp job start "*) echo execution ;;
  "containerapp job execution show "*) echo "${TEST_JOB_STATUS:-Succeeded}" ;;
  "containerapp update "*|"containerapp job update "*|"tag update "*) : ;;
  "bicep build "*) echo '{"resources":[]}' ;;
  "deployment group what-if "*) jq -n --arg change "${TEST_CHANGE:-Create}" \
    --argjson diagnostics "${TEST_DIAGNOSTICS:-[]}" \
    '{changes:[{resourceId:"/new",changeType:$change}],diagnostics:$diagnostics}' ;;
  "deployment group create "*) : ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$test_dir/gh" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  api)
    jq -n --arg path "${TEST_WORKFLOW:-.github/workflows/ci.yml}" --arg sha "${TEST_RUN_SHA:-$GITHUB_SHA}" \
      --arg conclusion "${TEST_CONCLUSION:-success}" \
      '{path:$path,head_branch:"main",event:"workflow_dispatch",head_sha:$sha,status:"completed",conclusion:$conclusion,run_attempt:1}' ;;
  run)
    if [[ "$*" == *"infra-preview-1"* ]]; then
      cp "$TEST_DIR/infra-preview.json" "${@: -1}/infra-preview.json"
    else
      cp "$TEST_DIR/manifest.json" "${@: -1}/release-manifest.json"
    fi ;;
  *) exit 1 ;;
esac
MOCK
cat > "$test_dir/curl" <<'MOCK'
#!/usr/bin/env bash
exit "${TEST_CURL_EXIT:-0}"
MOCK
cat > "$test_dir/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$test_dir/az" "$test_dir/gh" "$test_dir/curl" "$test_dir/sleep"
export PATH="$test_dir:$PATH" TEST_DIR="$test_dir" TEST_LOG="$test_dir/log"
export AZURE_SUBSCRIPTION_ID=sub AZURE_RESOURCE_GROUP=rg AZURE_CONTAINER_REGISTRY=test
export GITHUB_REPOSITORY=owner/repo GITHUB_SHA RELEASE_RUN_ID=123
GITHUB_SHA="$(git rev-parse HEAD)"
export TEST_DIGEST="sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
export RELEASE_MANIFEST="$test_dir/manifest.json"

TEST_GROUP_EXISTS=false BUILD_ALL=true bash infra/deployment/build-images.sh > "$test_dir/output"
[[ ! -e "$RELEASE_MANIFEST" ]]
if grep -q '^acr build' "$TEST_LOG"; then echo "Built an image without a resource group." >&2; exit 1; fi

: > "$TEST_LOG"
TEST_REGISTRIES='[]' BUILD_ALL=true bash infra/deployment/build-images.sh > "$test_dir/output"
[[ ! -e "$RELEASE_MANIFEST" ]]
if grep -q '^acr build' "$TEST_LOG"; then echo "Built an image without a registry." >&2; exit 1; fi

: > "$TEST_LOG"
BASE_SHA="$GITHUB_SHA" bash infra/deployment/build-images.sh > "$test_dir/output"
[[ "$(jq '.images | length' "$RELEASE_MANIFEST")" == 0 ]]
if grep -q '^acr build' "$TEST_LOG"; then echo "Built unchanged images." >&2; exit 1; fi

: > "$TEST_LOG"
BUILD_ALL=true bash infra/deployment/build-images.sh > "$test_dir/output"
[[ "$(grep -c '^acr build ' "$TEST_LOG")" == 7 ]]
jq -e --arg sha "$GITHUB_SHA" '.commit == $sha and (.images | length == 7)' "$RELEASE_MANIFEST" >/dev/null

: > "$TEST_LOG"
RELEASE_SERVICE=products-api bash infra/deployment/release-service.sh > "$test_dir/output"
[[ "$(grep -c '^containerapp update ' "$TEST_LOG")" == 1 ]]
grep -q "^containerapp update --name products-api .*products-api@$TEST_DIGEST" "$TEST_LOG"
if grep -q '^containerapp job \|^deployment group ' "$TEST_LOG"; then echo "Release modified another target." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_CONCLUSION=failure RELEASE_SERVICE=products-api bash infra/deployment/release-service.sh > "$test_dir/output" 2>&1; then
  echo "An unsuccessful CI run was accepted." >&2; exit 1
fi
if grep -q '^containerapp update ' "$TEST_LOG"; then echo "An unverified run updated an app." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_WORKER_MODEL=legacy RELEASE_SERVICE=mortgage-api bash infra/deployment/release-service.sh > "$test_dir/output" 2>&1; then
  echo "A legacy mortgage consumer was accepted." >&2; exit 1
fi
if grep -q '^containerapp update ' "$TEST_LOG"; then echo "Legacy app was updated." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_RUN_SHA=0000000000000000000000000000000000000000 RELEASE_SERVICE=products-api \
  bash infra/deployment/release-service.sh > "$test_dir/output" 2>&1; then
  echo "A manifest from another commit was accepted." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_CURL_EXIT=22 RELEASE_SERVICE=mortgages-web bash infra/deployment/release-service.sh > "$test_dir/output" 2>&1; then
  echo "An unready public app was accepted." >&2; exit 1
fi
if grep -q '^tag update' "$TEST_LOG"; then echo "Unready app received a success tag." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_JOB_STATUS=Failed RELEASE_SERVICE=database-bootstrap bash infra/deployment/release-service.sh > "$test_dir/output" 2>&1; then
  echo "A failed bootstrap was accepted." >&2; exit 1
fi
if grep -q '^tag update\|^containerapp update ' "$TEST_LOG"; then echo "Failed migration changed an app or success tag." >&2; exit 1; fi

export INFRA_RESULT="$test_dir/infra-preview.json"
INFRA_ACTION=preview bash infra/deployment/infrastructure-change.sh > "$test_dir/output"
: > "$TEST_LOG"
TEST_WORKFLOW=.github/workflows/prototype-infrastructure.yml INFRA_ACTION=apply PREVIEW_RUN_ID=42 \
  bash infra/deployment/infrastructure-change.sh > "$test_dir/output"
grep -q '^deployment group create ' "$TEST_LOG"

: > "$TEST_LOG"
if TEST_CHANGE=Modify INFRA_ACTION=preview bash infra/deployment/infrastructure-change.sh > "$test_dir/output" 2>&1; then
  echo "A modification of an existing resource was accepted." >&2; exit 1
fi
if grep -q '^deployment group create ' "$TEST_LOG"; then echo "Unsafe infrastructure was applied." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_CHANGE=NoChange INFRA_ACTION=preview bash infra/deployment/infrastructure-change.sh > "$test_dir/output" 2>&1; then
  echo "Redeploying an existing resource was accepted." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_DIAGNOSTICS='[{"code":"Incomplete"}]' INFRA_ACTION=preview bash infra/deployment/infrastructure-change.sh > "$test_dir/output" 2>&1; then
  echo "An incomplete what-if result was accepted." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_CHANGE=Ignore TEST_WORKFLOW=.github/workflows/prototype-infrastructure.yml INFRA_ACTION=apply PREVIEW_RUN_ID=42 \
  bash infra/deployment/infrastructure-change.sh > "$test_dir/output" 2>&1; then
  echo "A changed preview was accepted." >&2; exit 1
fi
if grep -q '^deployment group create ' "$TEST_LOG"; then echo "Changed infrastructure preview was applied." >&2; exit 1; fi
echo "Release tests passed."
