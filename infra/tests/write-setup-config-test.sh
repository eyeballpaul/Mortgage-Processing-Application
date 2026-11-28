#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-setup-config-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/config.json" "$test_dir/output"; rmdir "$test_dir"' EXIT

config_file="$test_dir/config.json"
touch "$config_file"
valid_values=$'11111111-2222-3333-4444-555555555555\n66666666-7777-8888-9999-aaaaaaaaaaaa\nprototype-rg\nuksouth\nregistryname\nsql-server\nstorageaccount\napi-client\nstaff-client\nstaff-secret\naaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\nowner/repo\n'
printf '%s' "$valid_values" | bash "$root/infra/deployment/write-setup-config.sh" "$config_file" > "$test_dir/output"
jq -e '
  .subscriptionId == "11111111-2222-3333-4444-555555555555" and
  .tenantId == "66666666-7777-8888-9999-aaaaaaaaaaaa" and
  .resourceGroup == "prototype-rg" and
  .staffClientSecret == "staff-secret" and
  .productsApiKey == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" and
  .eligibilityApiKey == "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" and
  .githubRepository == "owner/repo" and
  .confirmed == true and .configureGitHub == true
' "$config_file" >/dev/null
[[ "$(python3 -c 'import os, sys; print(oct(os.stat(sys.argv[1]).st_mode & 0o777))' "$config_file")" == 0o600 ]]
[[ ! -s "$test_dir/output" ]]

if printf '%s' "${valid_values/11111111-2222-3333-4444-555555555555/not-a-subscription}" |
  bash "$root/infra/deployment/write-setup-config.sh" "$config_file" > "$test_dir/output" 2>&1; then
  echo "Expected invalid subscription input to be rejected." >&2
  exit 1
fi

echo "Private setup config tests passed."
