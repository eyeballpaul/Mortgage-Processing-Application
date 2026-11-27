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
      jq -n --arg name "$name" --arg image "test.azurecr.io/$image:$tag" --arg marker "$marker" \
        '{id:("/" + $name),tags:{"pd-deployed-commit":$marker},properties:{template:{containers:[{image:$image}]}}}'
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

: > "$TEST_LOG"
bash infra/deployment/deploy-existing.sh > "$test_dir/output"
[[ "$(grep -c '^acr build ' "$TEST_LOG")" == 6 ]]
[[ "$(grep -c '^containerapp update ' "$TEST_LOG")" == 5 ]]
[[ "$(grep -c '^tag update ' "$TEST_LOG")" == 6 ]]
grep -q 'curl .*https://staff-web.example/health/ready' "$TEST_LOG"
grep -q 'curl .*https://mortgages-web.example/health/dependencies' "$TEST_LOG"

: > "$TEST_LOG"
if TEST_JOB_STATUS=Failed bash infra/deployment/deploy-existing.sh > "$test_dir/output" 2>&1; then
  echo "A failed bootstrap should fail the deployment." >&2
  exit 1
fi
if grep -q '^containerapp update\|^tag update' "$TEST_LOG"; then
  echo "App updates or success tags occurred after a failed bootstrap." >&2
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
