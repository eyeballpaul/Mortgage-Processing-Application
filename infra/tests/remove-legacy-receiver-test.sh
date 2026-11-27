#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-legacy-role-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/roles" "$test_dir/roles.new" "$test_dir/log" "$test_dir/output"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "role assignment list "*) cat "$TEST_DIR/roles" ;;
  "role assignment delete --ids "*)
    if [[ "${TEST_FAIL_DELETE:-false}" == true ]]; then exit 1; fi
    jq --arg id "$5" 'map(select(.id != $id))' "$TEST_DIR/roles" > "$TEST_DIR/roles.new"
    mv "$TEST_DIR/roles.new" "$TEST_DIR/roles" ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
chmod +x "$test_dir/az"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log" TEST_DIR="$test_dir"
principal=11111111-2222-3333-4444-555555555555
queue=/subscriptions/sub/resourceGroups/group/providers/Microsoft.ServiceBus/namespaces/bus/queues/submission-notifications
assignment="$queue/providers/Microsoft.Authorization/roleAssignments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
receiver=/subscriptions/sub/providers/Microsoft.Authorization/roleDefinitions/4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0
sender=/subscriptions/sub/providers/Microsoft.Authorization/roleDefinitions/69a216fc-b8fb-44d8-bc22-1f3c2cd27a39

reset_state() {
  : > "$TEST_LOG"
  jq -n --arg id "$assignment" --arg scope "$queue" --arg principal "$principal" --arg receiver "$receiver" --arg sender "$sender" '
    [{id:$id,scope:$scope,principalId:$principal,roleDefinitionId:$receiver},
     {id:($id + "-sender"),scope:$scope,principalId:$principal,roleDefinitionId:$sender}]' > "$TEST_DIR/roles"
}

reset_state
bash "$root/infra/deployment/remove-legacy-receiver.sh" "$principal" "$queue" > "$test_dir/output"
jq -e --arg sender "$sender" 'length == 1 and .[0].roleDefinitionId == $sender' "$TEST_DIR/roles" >/dev/null
grep -q "^role assignment delete --ids $assignment" "$TEST_LOG"
bash "$root/infra/deployment/remove-legacy-receiver.sh" "$principal" "$queue" > "$test_dir/output"
[[ "$(grep -c '^role assignment delete' "$TEST_LOG")" == 1 ]]

for scenario in inherited wrong-id delete-failure invalid-list; do
  reset_state
  case "$scenario" in
    inherited) jq '.[0].scope = "/subscriptions/sub"' "$TEST_DIR/roles" > "$TEST_DIR/roles.new"; mv "$TEST_DIR/roles.new" "$TEST_DIR/roles" ;;
    wrong-id) jq '.[0].id |= sub("namespaces/bus/"; "namespaces/other/")' "$TEST_DIR/roles" > "$TEST_DIR/roles.new"; mv "$TEST_DIR/roles.new" "$TEST_DIR/roles" ;;
    invalid-list) echo '{}' > "$TEST_DIR/roles" ;;
  esac
  if TEST_FAIL_DELETE=$([[ "$scenario" == delete-failure ]] && echo true || echo false) \
    bash "$root/infra/deployment/remove-legacy-receiver.sh" "$principal" "$queue" > "$test_dir/output" 2>&1; then
    echo "Expected $scenario to stop role cleanup." >&2; exit 1
  fi
  if [[ "$scenario" != delete-failure ]] && grep -q '^role assignment delete' "$TEST_LOG"; then
    echo "Deleted an assignment despite $scenario." >&2; exit 1
  fi
done
echo "Legacy receiver role mock tests passed."
