#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-stack-test.XXXXXXXX")"
trap 'rm -f -- "$dir/az" "$dir/log" "$dir/output" "$dir/template.json" "$dir/parameters.json" "$dir/config.json" "$dir/updated-stack"; rmdir "$dir"' EXIT

cat > "$dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show "*) echo sub ;;
  "group show "*) echo /subscriptions/sub/resourceGroups/rg ;;
  "acr show "*) echo test.azurecr.io ;;
  "containerapp list "*) if [[ -n "${TEST_APPS:-}" ]]; then echo "$TEST_APPS"; else echo '[{"name":"eligibility-api"}]'; fi ;;
  "containerapp job list "*) echo "${TEST_JOBS:-[]}" ;;
  "containerapp show "*)
    name="$6"
    jq -n --arg id "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/containerApps/$name" \
      --arg image "test.azurecr.io/$([[ "$name" == mortgage-demo-env-outbox-publisher ]] && echo outbox-publisher || echo eligibility-api):latest" \
      '{id:$id,tags:{application:"pd-mortgages-demo",managedBy:"bicep"},properties:{template:{containers:[{image:$image}]}}}' ;;
  "stack group list "*) echo "${TEST_STACKS:-[]}" ;;
  "stack group show "*)
    if [[ -f "$TEST_DIR/updated-stack" ]]; then cat "$TEST_DIR/updated-stack"; else echo "$TEST_STACK"; fi ;;
  "stack group create "*)
    if [[ "$*" == *"pd-app-outbox-publisher"* ]]; then
      jq --arg resource "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/containerApps/mortgage-demo-env-outbox-publisher" \
        '.resources[0].id = $resource' <<< "$TEST_STACK" > "$TEST_DIR/updated-stack"
    fi ;;
  "stack group delete "*) : ;;
  "bicep build "*) cat "$TEST_DIR/template.json" ;;
  *) echo "Unexpected Azure command: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$dir/az"
export PATH="$dir:$PATH" TEST_DIR="$dir" TEST_LOG="$dir/log"
export AZURE_SUBSCRIPTION_ID=sub AZURE_RESOURCE_GROUP=rg AZURE_CONTAINER_REGISTRY=test
export STACK_SERVICE=eligibility-api STACK_IMAGE=test.azurecr.io/eligibility-api:latest
export STACK_PARAMETERS="$dir/parameters.json"
cat > "$dir/template.json" <<'JSON'
{"resources":[{"type":"Microsoft.App/containerApps","name":"[parameters('name')]"}]}
JSON
cat > "$dir/parameters.json" <<'JSON'
{"parameters":{"name":{"value":"eligibility-api"},"imageRef":{"value":"test.azurecr.io/eligibility-api:latest"},"tags":{"value":{"application":"pd-mortgages-demo","managedBy":"bicep"}}}}
JSON
group=/subscriptions/sub/resourceGroups/rg
resource="$group/providers/Microsoft.App/containerApps/eligibility-api"
jq -cS '.parameters | to_entries | map(select(.key != "imageRef" and .key != "tags")) | from_entries' \
  "$STACK_PARAMETERS" > "$dir/config.json"
fingerprint="$(cat "$dir/template.json" "$dir/config.json" | openssl dgst -sha256 | awk '{print $NF}')"
stack_json() {
  jq -cn --arg group "$group" --arg target "$1" --arg resource "$2" --arg fingerprint "$3" \
    '{id:($group + "/providers/Microsoft.Resources/deploymentStacks/pd-app-" + $target),
      tags:{"pd-owner":"mortgage-prototype","pd-target":$target,"pd-template-sha":$fingerprint},
      resources:[{status:"managed",id:$resource}]}'
}
export TEST_STACK
TEST_STACK="$(stack_json eligibility-api "$resource" "$fingerprint")"

: > "$TEST_LOG"
if STACK_ACTION=apply bash infra/deployment/app-stack.sh > "$dir/output" 2>&1; then
  echo "Unplanned stack adoption was accepted." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unplanned stack was created." >&2; exit 1; fi

: > "$TEST_LOG"
ALLOW_CREATE=true STACK_ACTION=apply bash infra/deployment/app-stack.sh > "$dir/output"
grep -q '^stack group create ' "$TEST_LOG"

: > "$TEST_LOG"
TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' STACK_ACTION=apply \
  bash infra/deployment/app-stack.sh > "$dir/output"
if grep -q '^stack group create ' "$TEST_LOG"; then
  echo "An unchanged stack was reapplied." >&2; exit 1
fi

jq '.parameters = {"secret":{"type":"secureString"}}' "$dir/template.json" > "$dir/output"
mv "$dir/output" "$dir/template.json"
jq '.parameters.secret = {"value":"first-secret"}' "$dir/parameters.json" > "$dir/output"
mv "$dir/output" "$dir/parameters.json"
export DEPLOYMENT_FINGERPRINT_KEY=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
secret_fingerprint="$(ruby -ropenssl -rjson -e 'print OpenSSL::HMAC.hexdigest("SHA256", [ENV.fetch("DEPLOYMENT_FINGERPRINT_KEY")].pack("H*"), JSON.generate([["secret", "first-secret"]]))')"
secure_fingerprint="$({ cat "$dir/template.json" "$dir/config.json"; printf '%s' "$secret_fingerprint"; } | openssl dgst -sha256 | awk '{print $NF}')"
: > "$TEST_LOG"
if DEPLOYMENT_FINGERPRINT_KEY='' TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' \
  STACK_ACTION=apply bash infra/deployment/app-stack.sh > "$dir/output" 2>&1; then
  echo "Missing secret fingerprint key was accepted." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "Stack changed without a fingerprint key." >&2; exit 1; fi
: > "$TEST_LOG"
TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' \
  TEST_STACK="$(stack_json eligibility-api "$resource" "$secure_fingerprint")" \
  STACK_ACTION=apply bash infra/deployment/app-stack.sh > "$dir/output"
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unchanged secret caused an update." >&2; exit 1; fi
jq '.parameters.secret.value = "rotated-secret"' "$dir/parameters.json" > "$dir/output"
mv "$dir/output" "$dir/parameters.json"
: > "$TEST_LOG"
TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' \
  TEST_STACK="$(stack_json eligibility-api "$resource" "$secure_fingerprint")" \
  STACK_ACTION=apply bash infra/deployment/app-stack.sh > "$dir/output"
grep -q '^stack group create ' "$TEST_LOG"
if grep -q 'first-secret\|rotated-secret\|0123456789abcdef0123456789abcdef' "$TEST_LOG" "$dir/output"; then
  echo "Secret material appeared in output." >&2; exit 1
fi
jq 'del(.parameters)' "$dir/template.json" > "$dir/output"
mv "$dir/output" "$dir/template.json"
jq 'del(.parameters.secret)' "$dir/parameters.json" > "$dir/output"
mv "$dir/output" "$dir/parameters.json"

: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' \
  TEST_STACK="$(stack_json eligibility-api "$group/providers/Microsoft.App/containerApps/unrelated" "$fingerprint")" \
  STACK_ACTION=apply bash infra/deployment/app-stack.sh > "$dir/output" 2>&1; then
  echo "A stack managing an unrelated app was accepted." >&2; exit 1
fi
if grep -q '^stack group create \|^stack group delete ' "$TEST_LOG"; then
  echo "An unverified stack was changed." >&2; exit 1
fi

: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' TEST_APPS='[]' STACK_ACTION=apply \
  bash infra/deployment/app-stack.sh > "$dir/output" 2>&1; then
  echo "An absent app was created without authorization." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unplanned app was created." >&2; exit 1; fi

jq '.resources[0].condition = false' "$dir/template.json" > "$dir/output"
mv "$dir/output" "$dir/template.json"
: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' STACK_ACTION=apply \
  bash infra/deployment/app-stack.sh > "$dir/output" 2>&1; then
  echo "A conditional application declaration was accepted." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unsafe template was applied." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-app-eligibility-api"}]' STACK_ACTION=delete \
  bash infra/deployment/app-stack.sh > "$dir/output" 2>&1; then
  echo "A declared application was deleted." >&2; exit 1
fi
if grep -q '^stack group delete ' "$TEST_LOG"; then echo "A declared stack was deleted." >&2; exit 1; fi

: > "$TEST_LOG"
cat > "$dir/template.json" <<'JSON'
{"resources":[{"type":"Microsoft.App/containerApps","name":"[format('{0}-outbox-publisher', parameters('environmentName'))]"}]}
JSON
cat > "$dir/parameters.json" <<'JSON'
{"parameters":{"environmentName":{"value":"mortgage-demo-env"},"imageRef":{"value":"test.azurecr.io/outbox-publisher:latest"},"appTags":{"value":{"application":"pd-mortgages-demo","managedBy":"bicep"}}}}
JSON
TEST_APPS='[{"name":"mortgage-demo-env-outbox-publisher"}]' \
  TEST_STACKS='[{"name":"pd-app-outbox-publisher"}]' \
  TEST_STACK="$(stack_json outbox-publisher "$group/providers/Microsoft.App/jobs/mortgage-demo-env-outbox-publisher" "$fingerprint")" \
  STACK_SERVICE=outbox-publisher STACK_IMAGE=test.azurecr.io/outbox-publisher:latest \
  STACK_PARAMETERS="$dir/parameters.json" STACK_ACTION=apply ALLOW_CREATE=true \
  bash infra/deployment/app-stack.sh > "$dir/output"
grep -q '^stack group create ' "$TEST_LOG"
grep -q -- '--action-on-unmanage deleteResources' "$TEST_LOG"
rm -f -- "$dir/updated-stack"

: > "$TEST_LOG"
TEST_STACKS='[{"name":"pd-app-retired-app"}]' \
  TEST_STACK="$(stack_json retired-app "$group/providers/Microsoft.App/containerApps/retired-app" "$fingerprint")" \
  STACK_SERVICE=retired-app STACK_ACTION=delete bash infra/deployment/app-stack.sh > "$dir/output"
grep -q '^stack group delete .*--action-on-unmanage deleteResources' "$TEST_LOG"

: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-app-retired-app"}]' \
  TEST_STACK="$(stack_json retired-app "$group/providers/Microsoft.Sql/servers/unrelated" "$fingerprint")" \
  STACK_SERVICE=retired-app STACK_ACTION=delete bash infra/deployment/app-stack.sh > "$dir/output" 2>&1; then
  echo "A removed stack containing an unrelated resource was accepted." >&2; exit 1
fi
if grep -q '^stack group delete ' "$TEST_LOG"; then echo "An unsafe stack was deleted." >&2; exit 1; fi

echo "Application stack tests passed."
