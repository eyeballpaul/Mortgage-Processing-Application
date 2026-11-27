#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-foundation-test.XXXXXXXX")"
trap 'rm -f -- "$dir/az" "$dir/log" "$dir/output" "$dir/template.json" "$dir/parameters.json" "$dir/canonical.json"; rmdir "$dir"' EXIT

cat > "$dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show "*) echo sub ;;
  "group show "*) echo /subscriptions/sub/resourceGroups/rg ;;
  "stack group list "*) echo "${TEST_STACKS:-[]}" ;;
  "stack group show "*) echo "$TEST_STACK" ;;
  "stack group create "*) : ;;
  "bicep build "*) cat "$TEST_DIR/template.json" ;;
  *) echo "Unexpected Azure command: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$dir/az"
export PATH="$dir:$PATH" TEST_DIR="$dir" TEST_LOG="$dir/log"
export AZURE_SUBSCRIPTION_ID=sub AZURE_RESOURCE_GROUP=rg STACK_PARAMETERS="$dir/parameters.json"
cat > "$dir/template.json" <<'JSON'
{"resources":[{"type":"Microsoft.Network/virtualNetworks","name":"demo"}]}
JSON
cat > "$dir/parameters.json" <<'JSON'
{"parameters":{"createSqlServer":{"value":false},"deployAlerts":{"value":true}}}
JSON
jq -cS '.parameters' "$STACK_PARAMETERS" > "$dir/canonical.json"
fingerprint="$(cat "$dir/template.json" "$dir/canonical.json" | openssl dgst -sha256 | awk '{print $NF}')"
group=/subscriptions/sub/resourceGroups/rg
stack_json() {
  jq -cn --arg group "$group" --arg resource "$1" --arg fingerprint "$2" \
    '{id:($group + "/providers/Microsoft.Resources/deploymentStacks/pd-foundation"),
      tags:{"pd-owner":"mortgage-prototype","pd-target":"foundation","pd-template-sha":$fingerprint},
      resources:[{status:"managed",id:$resource}]}'
}
export TEST_STACK
TEST_STACK="$(stack_json "$group/providers/Microsoft.Network/virtualNetworks/demo" "$fingerprint")"

: > "$TEST_LOG"
if bash infra/deployment/foundation-stack.sh > "$dir/output" 2>&1; then
  echo "An uninitialized foundation was accepted." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unplanned foundation was created." >&2; exit 1; fi

: > "$TEST_LOG"
ALLOW_CREATE=true bash infra/deployment/foundation-stack.sh > "$dir/output"
grep -q '^stack group create .*--action-on-unmanage deleteResources' "$TEST_LOG"

: > "$TEST_LOG"
TEST_STACKS='[{"name":"pd-foundation"}]' bash infra/deployment/foundation-stack.sh > "$dir/output"
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unchanged foundation was reapplied." >&2; exit 1; fi

: > "$TEST_LOG"
TEST_STACKS='[{"name":"pd-foundation"}]' \
  TEST_STACK="$(stack_json "$group/providers/Microsoft.Network/virtualNetworks/demo" "different")" \
  bash infra/deployment/foundation-stack.sh > "$dir/output"
grep -q '^stack group create .*--action-on-unmanage deleteResources' "$TEST_LOG"

: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-foundation"}]' \
  TEST_STACK="$(stack_json "$group/providers/Microsoft.App/containerApps/other" "$fingerprint")" \
  bash infra/deployment/foundation-stack.sh > "$dir/output" 2>&1; then
  echo "A foundation stack owning an app was accepted." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unsafe foundation was applied." >&2; exit 1; fi

: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-foundation"}]' \
  TEST_STACK="$(stack_json "/subscriptions/other/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/demo" "$fingerprint")" \
  bash infra/deployment/foundation-stack.sh > "$dir/output" 2>&1; then
  echo "An out-of-scope resource was accepted." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An out-of-scope stack was applied." >&2; exit 1; fi

jq '.resources += [{"type":"Microsoft.App/containerApps","name":"unexpected"}]' "$dir/template.json" > "$dir/output"
mv "$dir/output" "$dir/template.json"
: > "$TEST_LOG"
if TEST_STACKS='[{"name":"pd-foundation"}]' bash infra/deployment/foundation-stack.sh > "$dir/output" 2>&1; then
  echo "An application in the foundation template was accepted." >&2; exit 1
fi
if grep -q '^stack group create ' "$TEST_LOG"; then echo "An unsafe template was applied." >&2; exit 1; fi
echo "Foundation stack tests passed."
