#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-callback-test.XXXXXXXX")"
trap 'rm -f -- "$dir/az" "$dir/log" "$dir/state" "$dir/output"; rmdir "$dir"' EXIT
cat > "$dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "containerapp show "*) echo '{"properties":{"configuration":{"ingress":{"fqdn":"staff.example"}},"template":{"containers":[{"env":[{"name":"AzureAd__ClientId","value":"staff-client"}]}]}}}' ;;
  "ad app show "*) cat "$TEST_DIR/state" ;;
  "rest --method patch "*)
    while [[ $# -gt 0 ]]; do
      if [[ "$1" == --body ]]; then body="$2"; break; fi
      shift
    done
    jq -n --argjson body "$body" '{id:"object",web:$body.web}' > "$TEST_DIR/state" ;;
  *) echo "Unexpected command: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$dir/az"
export PATH="$dir:$PATH" TEST_DIR="$dir" TEST_LOG="$dir/log" AZURE_RESOURCE_GROUP=rg STAFF_CLIENT_ID=staff-client
echo '{"id":"object","web":{"homePageUrl":"https://existing.example","redirectUris":["https://localhost/signin-oidc"]}}' > "$dir/state"
: > "$TEST_LOG"
bash infra/deployment/sync-staff-callbacks.sh > "$dir/output"
jq -e '.web.homePageUrl == "https://existing.example" and
  (.web.redirectUris | index("https://localhost/signin-oidc") != null and
    index("https://staff.example/signin-oidc") != null and
    index("https://staff.example/signout-callback-oidc") != null)' "$dir/state" >/dev/null
grep -q '^rest --method patch ' "$TEST_LOG"
: > "$TEST_LOG"
bash infra/deployment/sync-staff-callbacks.sh > "$dir/output"
if grep -q '^rest --method patch ' "$TEST_LOG"; then echo "Unchanged callbacks were patched." >&2; exit 1; fi
STAFF_CALLBACK_ACTION=remove bash infra/deployment/sync-staff-callbacks.sh > "$dir/output"
jq -e '.web.redirectUris == ["https://localhost/signin-oidc"] and .web.homePageUrl == "https://existing.example"' "$dir/state" >/dev/null
grep -q '^rest --method patch ' "$TEST_LOG"
echo "Staff callback synchronization tests passed."
