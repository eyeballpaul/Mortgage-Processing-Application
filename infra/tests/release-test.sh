#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-release-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/gh" "$test_dir/git" "$test_dir/curl" "$test_dir/sleep" "$test_dir/log" "$test_dir/output" "$test_dir/manifest.json" "$test_dir/manifest-backup.json" "$test_dir/manifest-new.json" "$test_dir/app-plan.json" "$test_dir/infra-preview.json"; rmdir "$test_dir"' EXIT

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
  "containerapp list "*) jq -n --arg missing "${TEST_MISSING_SERVICE:-}" \
    '["products-api","eligibility-api","mortgage-api","notifications-worker","mortgages-web","staff-web"] | map(select(. != $missing) | {name:.})' ;;
  "containerapp show "*)
    if [[ "$*" == *"--query properties.configuration.ingress.fqdn"* ]]; then
      echo app.example
    else
      name="$4"
      case "$name" in mortgage-api) repository=mortgages-api ;; staff-web) repository=mortgages-staff-web ;; *) repository="$name" ;; esac
      baseline="${TEST_BASELINE_ALL:-}"
      if [[ "${TEST_BEHIND_SERVICE:-}" == "$name" ]]; then baseline="$TEST_BEHIND_COMMIT"; fi
      if [[ "${TEST_UNVERIFIED_SERVICE:-}" == "$name" ]]; then baseline=""; fi
      digest="${TEST_BASELINE_DIGEST:-}"
      image="test.azurecr.io/$repository@$TEST_DIGEST"
      if [[ "${TEST_DRIFT_SERVICE:-}" == "$name" ]]; then image="test.azurecr.io/$repository:drift"; fi
      jq -n --arg name "$name" --arg image "$image" --arg baseline "$baseline" --arg digest "$digest" \
        --arg worker "${TEST_WORKER_MODEL:-separate}" \
        '{id:("/app/" + $name),tags:{"pd-deployed-commit":$baseline,"pd-deployed-digest":$digest},
          identity:{userAssignedIdentities:{"/identity":{}}},properties:{configuration:{activeRevisionsMode:"Single"},template:{containers:[{image:$image,env:[{name:"AZURE_CLIENT_ID",value:"client"},{name:"Notifications__WorkerModel",value:$worker}]}]}}}'
    fi ;;
  "containerapp revision list "*)
    name="$5"
    case "$name" in mortgage-api) repository=mortgages-api ;; staff-web) repository=mortgages-staff-web ;; *) repository="$name" ;; esac
    image="test.azurecr.io/$repository@$TEST_DIGEST"
    if [[ "${TEST_REVISION_DRIFT_SERVICE:-}" == "$name" ]]; then image="test.azurecr.io/$repository:drift"; fi
    jq -n --arg name "$name" --arg image "$image" '[{name:($name + "--revision"),properties:{active:true,template:{containers:[{image:$image}]}}}]' ;;
  "identity show "*) echo '{"id":"/identity","clientId":"client"}' ;;
  "containerapp job list "*)
    if [[ "${TEST_JOB_MISSING:-false}" == true ]]; then echo '[]'
    else echo '[{"name":"mortgage-demo-env-bootstrap"}]'; fi ;;
  "containerapp job show "*)
    if [[ "$*" == *"--query id"* ]]; then
      echo /job
    else
      jq -n --arg image "${TEST_JOB_IMAGE:-test.azurecr.io/database-bootstrap:latest}" \
        --arg commit "${TEST_JOB_COMMIT:-}" --arg digest "${TEST_JOB_DIGEST:-}" \
        --arg job_arg "${TEST_JOB_ARG:-original}" \
        '{id:"/job",tags:{"pd-bootstrap-commit":$commit,"pd-bootstrap-digest":$digest},
          properties:{template:{containers:[{image:$image,args:[$job_arg]}]}}}'
    fi ;;
  "containerapp job start "*) echo execution ;;
  "containerapp job execution show "*) echo "${TEST_JOB_STATUS:-Succeeded}" ;;
  "containerapp update "*)
    if [[ "${TEST_FAIL_SERVICE:-}" == "$4" ]]; then echo "Update failed" >&2; exit 1; fi ;;
  "containerapp job update "*|"tag update "*) : ;;
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
    if [[ "$2" == */git/ref/heads/main ]]; then echo "${TEST_MAIN_SHA:-$GITHUB_SHA}"; exit 0; fi
    jq -n --arg path "${TEST_WORKFLOW:-.github/workflows/ci.yml}" --arg sha "${TEST_RUN_SHA:-$GITHUB_SHA}" \
      --arg conclusion "${TEST_CONCLUSION:-success}" \
      '{path:$path,head_branch:"main",event:"workflow_dispatch",head_sha:$sha,status:"completed",conclusion:$conclusion,run_attempt:1}' ;;
  run)
    if [[ "$*" == *"infra-preview-1"* ]]; then
      cp "$TEST_DIR/infra-preview.json" "${@: -1}/infra-preview.json"
    elif [[ "$*" == *"app-plan-1"* ]]; then
      cp "$TEST_DIR/app-plan.json" "${@: -1}/app-plan.json"
    else
      cp "$TEST_DIR/manifest.json" "${@: -1}/release-manifest.json"
    fi ;;
  *) exit 1 ;;
esac
MOCK
cat > "$test_dir/git" <<'MOCK'
#!/usr/bin/env bash
if [[ -n "${TEST_BEHIND_COMMIT:-}" && "$*" == *"$TEST_BEHIND_COMMIT"* ]]; then
  case "$1" in
    merge-base) exit 0 ;;
    diff) printf 'PD.Products.Api/Program.cs\0'; exit 0 ;;
  esac
fi
exec "$TEST_REAL_GIT" "$@"
MOCK
cat > "$test_dir/curl" <<'MOCK'
#!/usr/bin/env bash
exit "${TEST_CURL_EXIT:-0}"
MOCK
cat > "$test_dir/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
export TEST_REAL_GIT
TEST_REAL_GIT="$(command -v git)"
chmod +x "$test_dir/az" "$test_dir/gh" "$test_dir/git" "$test_dir/curl" "$test_dir/sleep"
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

export APP_PLAN="$test_dir/app-plan.json" TEST_BASELINE_ALL="$GITHUB_SHA" TEST_BASELINE_DIGEST="$TEST_DIGEST"
export TEST_BEHIND_SERVICE=products-api TEST_BEHIND_COMMIT=1111111111111111111111111111111111111111
: > "$TEST_LOG"
TEST_BASELINE_ALL= TEST_BASELINE_DIGEST= TEST_BEHIND_SERVICE= bash infra/deployment/plan-apps.sh > "$test_dir/output"
jq -e '[.targets[] | select(.status == "deploy")] | length == 6' "$APP_PLAN" >/dev/null

: > "$TEST_LOG"
TEST_REVISION_DRIFT_SERVICE=staff-web bash infra/deployment/plan-apps.sh > "$test_dir/output"
jq -e '.targets[] | select(.service == "staff-web" and .status == "deploy")' "$APP_PLAN" >/dev/null

: > "$TEST_LOG"
bash infra/deployment/plan-apps.sh > "$test_dir/output"
jq -e '[.targets[] | select(.status == "deploy") | .service] == ["products-api"]' "$APP_PLAN" >/dev/null
BASE_SHA="$GITHUB_SHA" bash infra/deployment/build-images.sh > "$test_dir/output"
[[ "$(grep -c '^acr build ' "$TEST_LOG")" == 2 ]]
grep -q -- '--image products-api:' "$TEST_LOG"
grep -q -- '--image database-bootstrap:' "$TEST_LOG"
jq -e --slurpfile manifest "$RELEASE_MANIFEST" '
  .commit == $manifest[0].commit and .registry == $manifest[0].registry and
  all(.targets[] | select(.status == "deploy" or .status == "missing"); .service as $s |
    ($manifest[0].images[$s].digest | type == "string" and test("^sha256:[0-9a-f]{64}$")))
' "$APP_PLAN" >/dev/null

: > "$TEST_LOG"
if REVIEW_CONFIRMED=false bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A plan without review confirmation was accepted." >&2; exit 1
fi
if grep -q '^containerapp update ' "$TEST_LOG"; then echo "An unreviewed plan updated an app." >&2; exit 1; fi

: > "$TEST_LOG"
REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output"
[[ "$(grep -c '^containerapp update ' "$TEST_LOG")" == 1 ]]
grep -q '^containerapp update --name products-api ' "$TEST_LOG"
grep -q "^tag update .*pd-deployed-digest=$TEST_DIGEST" "$TEST_LOG"
grep -q "^tag update .*pd-bootstrap-digest=$TEST_DIGEST" "$TEST_LOG"
[[ "$(grep -n '^containerapp job start ' "$TEST_LOG" | cut -d: -f1)" -lt "$(grep -n '^containerapp update ' "$TEST_LOG" | cut -d: -f1)" ]]

: > "$TEST_LOG"
if TEST_JOB_STATUS=Failed REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A failed bootstrap was accepted during plan application." >&2; exit 1
fi
if grep -q '^containerapp update \|^tag update ' "$TEST_LOG"; then
  echo "Failed bootstrap allowed an app update or success tag." >&2; exit 1
fi

cp "$RELEASE_MANIFEST" "$test_dir/manifest-backup.json"
jq 'del(.images["database-bootstrap"])' "$RELEASE_MANIFEST" > "$test_dir/manifest-new.json"
mv "$test_dir/manifest-new.json" "$RELEASE_MANIFEST"
: > "$TEST_LOG"
if REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A plan without a verified bootstrap image was accepted." >&2; exit 1
fi
if grep -q '^containerapp update \|^containerapp job update ' "$TEST_LOG"; then
  echo "A missing bootstrap image allowed updates." >&2; exit 1
fi
mv "$test_dir/manifest-backup.json" "$RELEASE_MANIFEST"

: > "$TEST_LOG"
if TEST_JOB_IMAGE=test.azurecr.io/database-bootstrap:drift REVIEW_CONFIRMED=true \
  bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A changed bootstrap job was accepted." >&2; exit 1
fi
if grep -q '^containerapp update \|^containerapp job update ' "$TEST_LOG"; then
  echo "A stale bootstrap plan allowed updates." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_JOB_ARG=drift REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "Changed bootstrap configuration was accepted." >&2; exit 1
fi
if grep -q '^containerapp update \|^containerapp job update ' "$TEST_LOG"; then
  echo "Changed bootstrap configuration allowed updates." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_JOB_COMMIT=0000000000000000000000000000000000000000 \
  bash infra/deployment/plan-apps.sh > "$test_dir/output" 2>&1; then
  echo "A non-ancestor bootstrap baseline was accepted." >&2; exit 1
fi

TEST_JOB_IMAGE="test.azurecr.io/database-bootstrap@$TEST_DIGEST" \
  TEST_JOB_COMMIT="$GITHUB_SHA" TEST_JOB_DIGEST="$TEST_DIGEST" \
  bash infra/deployment/plan-apps.sh > "$test_dir/output"
jq -e '.bootstrapBuild == false' "$APP_PLAN" >/dev/null
TEST_JOB_IMAGE="test.azurecr.io/database-bootstrap@$TEST_DIGEST" \
  TEST_JOB_COMMIT="$GITHUB_SHA" TEST_JOB_DIGEST="$TEST_DIGEST" \
  BASE_SHA="$GITHUB_SHA" bash infra/deployment/build-images.sh > "$test_dir/output"
: > "$TEST_LOG"
TEST_JOB_IMAGE="test.azurecr.io/database-bootstrap@$TEST_DIGEST" \
  TEST_JOB_COMMIT="$GITHUB_SHA" TEST_JOB_DIGEST="$TEST_DIGEST" REVIEW_CONFIRMED=true \
  bash infra/deployment/apply-app-plan.sh > "$test_dir/output"
if grep -q '^containerapp job update \|^containerapp job start ' "$TEST_LOG"; then
  echo "An unchanged bootstrap was run." >&2; exit 1
fi

bash infra/deployment/plan-apps.sh > "$test_dir/output"
BASE_SHA="$GITHUB_SHA" bash infra/deployment/build-images.sh > "$test_dir/output"

: > "$TEST_LOG"
if TEST_DRIFT_SERVICE=staff-web REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A stale deployment plan was accepted." >&2; exit 1
fi
if grep -q '^containerapp update ' "$TEST_LOG"; then echo "A stale plan updated an app." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_REVISION_DRIFT_SERVICE=staff-web REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A changed active revision was accepted." >&2; exit 1
fi
if grep -q '^containerapp update ' "$TEST_LOG"; then echo "A changed active revision allowed updates." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_MAIN_SHA=0000000000000000000000000000000000000000 REVIEW_CONFIRMED=true \
  bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A plan for an outdated main commit was accepted." >&2; exit 1
fi
if grep -q '^containerapp update ' "$TEST_LOG"; then echo "An outdated plan updated an app." >&2; exit 1; fi

export TEST_UNVERIFIED_SERVICE=eligibility-api
bash infra/deployment/plan-apps.sh > "$test_dir/output"
BASE_SHA="$GITHUB_SHA" bash infra/deployment/build-images.sh > "$test_dir/output"
: > "$TEST_LOG"
if TEST_FAIL_SERVICE=eligibility-api REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A failed app update was accepted." >&2; exit 1
fi
[[ "$(grep -c '^containerapp update ' "$TEST_LOG")" == 2 ]]
grep -q '^tag update --resource-id /app/products-api ' "$TEST_LOG"
if grep -q '^tag update --resource-id /app/eligibility-api ' "$TEST_LOG"; then
  echo "The failed app received a success tag." >&2; exit 1
fi
unset TEST_UNVERIFIED_SERVICE

TEST_JOB_MISSING=true bash infra/deployment/plan-apps.sh > "$test_dir/output"
jq -e '.bootstrapBuild == true and .bootstrap.sourceImage == ""' "$APP_PLAN" >/dev/null
BASE_SHA="$GITHUB_SHA" bash infra/deployment/build-images.sh > "$test_dir/output"
jq -e '.images["database-bootstrap"].digest != null' "$RELEASE_MANIFEST" >/dev/null

TEST_MISSING_SERVICE=staff-web bash infra/deployment/plan-apps.sh > "$test_dir/output"
jq -e '.targets[] | select(.service == "staff-web" and .status == "missing")' "$APP_PLAN" >/dev/null
BASE_SHA="$GITHUB_SHA" bash infra/deployment/build-images.sh > "$test_dir/output"
jq -e '.images["staff-web"].digest != null' "$RELEASE_MANIFEST" >/dev/null
jq -e --slurpfile manifest "$RELEASE_MANIFEST" '
  all(.targets[] | select(.status == "deploy" or .status == "missing"); .service as $s |
    ($manifest[0].images[$s].digest | type == "string" and test("^sha256:[0-9a-f]{64}$")))
' "$APP_PLAN" >/dev/null
: > "$TEST_LOG"
if REVIEW_CONFIRMED=true bash infra/deployment/apply-app-plan.sh > "$test_dir/output" 2>&1; then
  echo "A plan with a missing app was accepted." >&2; exit 1
fi
if grep -q '^containerapp update ' "$TEST_LOG"; then echo "A plan with a missing app updated an app." >&2; exit 1; fi
unset APP_PLAN TEST_BASELINE_ALL TEST_BASELINE_DIGEST TEST_BEHIND_SERVICE TEST_BEHIND_COMMIT

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
