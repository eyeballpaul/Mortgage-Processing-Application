#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-reconcile-test.XXXXXXXX")"
trap 'rm -f -- "$dir/az" "$dir/gh" "$dir/bash" "$dir/log" "$dir/output" "$dir/migrated"; rmdir "$dir"' EXIT

cat > "$dir/gh" <<'MOCK'
#!/bin/bash
set -euo pipefail
if [[ "$*" == *"/git/ref/heads/main"* ]]; then echo "$GITHUB_SHA"; exit; fi
jq -cn --arg sha "$GITHUB_SHA" --arg result "${TEST_CI_RESULT:-success}" \
  '{workflow_runs:[{path:".github/workflows/ci.yml",head_branch:"main",event:"push",
    head_sha:$sha,status:"completed",conclusion:$result}]}'
MOCK
cat > "$dir/az" <<'MOCK'
#!/bin/bash
set -euo pipefail
echo "az $*" >> "$TEST_LOG"
case "$*" in
  "stack group list "*) echo '[{"name":"pd-foundation"},{"name":"pd-app-retired","tags":{"pd-owner":"mortgage-prototype","pd-target":"retired"}}]' ;;
  "stack group show "*) jq -cn --arg sha "$TEST_FOUNDATION_SHA" --arg migrated "$(cat "$TEST_DIR/migrated")" \
    '{id:"/foundation",tags:{"pd-template-sha":$sha,"pd-migrated-sha":$migrated}}' ;;
  "acr show "*) echo registry.example ;;
  "containerapp job show "*) echo '{"properties":{"template":{"containers":[{"image":"registry.example/database-bootstrap:latest"}]}}}' ;;
  "containerapp show "*) echo '{"properties":{"template":{"containers":[{"image":"registry.example/test:latest"}]}}}' ;;
  *) echo "Unexpected Azure command: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$dir/bash" <<'MOCK'
#!/bin/bash
set -euo pipefail
echo "helper $* ${STACK_TARGET:-} ${STACK_SERVICE:-} ${STACK_ACTION:-} ${RELEASE_SERVICE:-} ${ALLOW_CREATE:-} ${STACK_MIGRATED_SHA:-}" >> "$TEST_LOG"
case "$1" in
  infra/deployment/stack-parameters.sh) echo '{}' > "$STACK_PARAMETERS" ;;
  infra/deployment/foundation-stack.sh)
    if [[ -n "${STACK_MIGRATED_SHA:-}" ]]; then
      printf '%s' "$STACK_MIGRATED_SHA" > "$TEST_DIR/migrated"
    fi
    ;;
  infra/deployment/plan-apps.sh)
    jq -cn --arg changed "${TEST_CHANGED_SERVICE:-}" --arg missing "${TEST_MISSING_SERVICE:-}" \
      --arg digest "$TEST_DIGEST" --arg bootstrap "${TEST_BOOTSTRAP_BUILD:-false}" --arg missing_job "${TEST_JOB_MISSING:-false}" '
      ["products-api","eligibility-api","mortgage-api","notifications-worker","mortgages-web","staff-web"] |
      {targets:map({service:.,status:(if . == $missing then "missing" elif . == $changed then "deploy" else "unchanged" end),
        sourceImage:("registry.example/" + . + "@" + $digest)}),
       bootstrapBuild:($bootstrap == "true" or $missing_job == "true"),
       bootstrap:{sourceImage:(if $missing_job == "true" then "" else "registry.example/database-bootstrap@" + $digest end),sourceDigest:$digest}}' > "$APP_PLAN" ;;
  infra/deployment/build-images.sh)
    jq -cn --arg sha "$GITHUB_SHA" --arg registry "$AZURE_CONTAINER_REGISTRY" --arg digest "$TEST_DIGEST" '
      {commit:$sha,registry:$registry,images:{
        "database-bootstrap":{repository:"database-bootstrap",digest:$digest},
        "mortgage-api":{repository:"mortgages-api",digest:$digest},
        "staff-web":{repository:"mortgages-staff-web",digest:$digest}}}' > "$RELEASE_MANIFEST" ;;
  infra/deployment/app-stack.sh)
    if [[ "${TEST_FAIL_SERVICE:-}" == "${STACK_SERVICE:-}" ]]; then exit 1; fi ;;
  infra/deployment/deploy-image.sh)
    if [[ "${TEST_FAIL_BOOTSTRAP:-false}" == true && "$RELEASE_SERVICE" == database-bootstrap ]]; then exit 1; fi ;;
  infra/deployment/sync-staff-callbacks.sh) : ;;
  *) echo "Unexpected helper: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$dir/gh" "$dir/az" "$dir/bash"
export PATH="$dir:$PATH" TEST_DIR="$dir" TEST_LOG="$dir/log"
export AZURE_SUBSCRIPTION_ID=sub AZURE_RESOURCE_GROUP=rg AZURE_CONTAINER_REGISTRY=registry
export AZURE_SQL_SERVER=sql AZURE_STORAGE_ACCOUNT=storage AZURE_CLIENT_ID=client
export MORTGAGE_API_CLIENT_ID=api STAFF_CLIENT_ID=staff STAFF_CLIENT_SECRET=staff-secret
export PRODUCTS_API_KEY=products-secret ELIGIBILITY_API_KEY=eligibility-secret
export DEPLOYMENT_FINGERPRINT_KEY=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
export GITHUB_REPOSITORY=owner/repo GITHUB_SHA=1111111111111111111111111111111111111111
export TEST_DIGEST=sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
export TEST_FOUNDATION_SHA=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb

printf '%s' "$TEST_FOUNDATION_SHA" > "$dir/migrated"
: > "$TEST_LOG"
TEST_CHANGED_SERVICE=mortgage-api TEST_MISSING_SERVICE=staff-web /bin/bash infra/deployment/reconcile-prototype.sh > "$dir/output"
if grep -q 'deploy-image.sh .*database-bootstrap' "$TEST_LOG"; then
  echo "An unchanged database was migrated." >&2; exit 1
fi
grep -q 'deploy-image.sh .*mortgage-api' "$TEST_LOG"
grep -q 'deploy-image.sh .*staff-web' "$TEST_LOG"
grep -q 'app-stack.sh .*retired delete' "$TEST_LOG"

: > "$TEST_LOG"
TEST_JOB_MISSING=true /bin/bash infra/deployment/reconcile-prototype.sh > "$dir/output"
grep -q 'app-stack.sh .*database-bootstrap apply .*true' "$TEST_LOG"
grep -q 'deploy-image.sh .*database-bootstrap' "$TEST_LOG"

printf '%s' old > "$dir/migrated"
: > "$TEST_LOG"
if TEST_FAIL_BOOTSTRAP=true /bin/bash infra/deployment/reconcile-prototype.sh > "$dir/output" 2>&1; then
  echo "A failed migration did not block deployment." >&2; exit 1
fi
if grep -q 'app-stack.sh .*products-api' "$TEST_LOG"; then
  echo "Applications changed after a failed migration." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_CHANGED_SERVICE=mortgage-api TEST_FAIL_SERVICE=mortgage-api \
  /bin/bash infra/deployment/reconcile-prototype.sh > "$dir/output" 2>&1; then
  echo "Expected a partial app failure." >&2; exit 1
fi
grep -q 'deploy-image.sh .*database-bootstrap' "$TEST_LOG"
[[ "$(cat "$dir/migrated")" == "$TEST_FOUNDATION_SHA" ]]
: > "$TEST_LOG"
TEST_CHANGED_SERVICE=mortgage-api /bin/bash infra/deployment/reconcile-prototype.sh > "$dir/output"
if grep -q 'deploy-image.sh .*database-bootstrap' "$TEST_LOG"; then
  echo "A completed migration was repeated on retry." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_CI_RESULT=failure /bin/bash infra/deployment/reconcile-prototype.sh > "$dir/output" 2>&1; then
  echo "An untested commit was deployed." >&2; exit 1
fi
if grep -q 'helper\|az stack group' "$TEST_LOG"; then echo "Untested commit changed Azure." >&2; exit 1; fi

: > "$TEST_LOG"
/bin/bash infra/deployment/initialize-stacks.sh > "$dir/output"
[[ "$(grep -c 'helper infra/deployment/app-stack.sh' "$TEST_LOG")" == 7 ]]
foundation_line="$(grep -n 'helper infra/deployment/foundation-stack.sh' "$TEST_LOG" | cut -d: -f1)"
bootstrap_line="$(grep -n 'helper infra/deployment/app-stack.sh .*database-bootstrap' "$TEST_LOG" | cut -d: -f1)"
[[ "$foundation_line" -lt "$bootstrap_line" ]]
echo "Prototype reconciliation orchestration tests passed."
