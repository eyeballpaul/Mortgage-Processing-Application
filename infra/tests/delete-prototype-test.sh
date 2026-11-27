#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-delete-test.XXXXXXXX")"
trap 'rm -f -- "$test_dir/az" "$test_dir/sleep" "$test_dir/resources" "$test_dir/resources.new" "$test_dir/links" "$test_dir/links.new" "$test_dir/staff" "$test_dir/staff.new" "$test_dir/log" "$test_dir/output"; rmdir "$test_dir"' EXIT

cat > "$test_dir/az" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$TEST_LOG"
case "$*" in
  "account show --query id --output tsv") echo "${TEST_SUBSCRIPTION:-subscription-id}" ;;
  "group exists --name test-rg --output tsv") echo true ;;
  "group show --name test-rg --query id --output tsv") echo /subscriptions/subscription-id/resourceGroups/test-rg ;;
  "resource list --resource-group test-rg --output json") cat "$TEST_STATE/resources" ;;
  "containerapp show --name staff-web --resource-group test-rg --output json")
    echo '{"properties":{"configuration":{"ingress":{"fqdn":"staff.example"}},"template":{"containers":[{"env":[{"name":"AzureAd__ClientId","value":"staff-client"}]}]}}}' ;;
  "ad app show --id staff-client --output json") cat "$TEST_STATE/staff" ;;
  "rest --method patch "*)
    if [[ "${TEST_FAIL_GRAPH:-false}" == true ]]; then exit 1; fi
    while [[ $# -gt 0 ]]; do
      if [[ "$1" == --body ]]; then body="$2"; break; fi
      shift
    done
    jq -e '.web.homePageUrl == "https://existing.example" and (.web | has("redirectUriSettings") | not) and .web.redirectUris == ["https://localhost:7241/signin-oidc"]' <<< "$body" >/dev/null
    jq --argjson body "$body" '.web = ($body.web + {redirectUriSettings: [$body.web.redirectUris[] | {uri: ., index: null}]})' "$TEST_STATE/staff" > "$TEST_STATE/staff.new"
    mv "$TEST_STATE/staff.new" "$TEST_STATE/staff" ;;
  "network nic show --ids "*)
    if [[ "${TEST_FOREIGN_NIC:-false}" == true ]]; then echo /subscriptions/elsewhere; else
      echo /subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.Network/privateEndpoints/sql-server-private
    fi ;;
  "network private-dns link vnet list --resource-group test-rg --zone-name "*" --output json")
    if [[ "${TEST_FAIL_LINK_LIST:-false}" == true ]]; then exit 1; fi
    jq --arg zone "$9" '[.[] | select(.zone == $zone) | del(.zone)]' "$TEST_STATE/links" ;;
  "network private-dns link vnet delete --ids "*" --yes")
    if [[ "${TEST_FAIL_LINK_DELETE:-false}" == true ]]; then exit 1; fi
    jq --arg id "$7" 'map(select(.id != $id))' "$TEST_STATE/links" > "$TEST_STATE/links.new"
    mv "$TEST_STATE/links.new" "$TEST_STATE/links" ;;
  "resource delete --ids "*)
    if [[ "${TEST_FAIL_DELETE:-false}" == true ]]; then exit 1; fi
    if [[ "${TEST_LEAVE_RESOURCE:-false}" == true ]]; then exit 0; fi
    id="$4"
    if [[ "$id" == *"/privateDnsZones/"* ]] && jq -e --arg zone "$id" \
      'any(.[]; .id | startswith($zone + "/"))' "$TEST_STATE/links" >/dev/null; then
      echo "DNS zone still has a virtual network link." >&2; exit 1
    fi
    jq --arg id "$id" 'map(select(.id != $id and
      (if ($id | endswith("/privateEndpoints/sql-server-private")) then .type != "Microsoft.Network/networkInterfaces" else true end)))' \
      "$TEST_STATE/resources" > "$TEST_STATE/resources.new"
    mv "$TEST_STATE/resources.new" "$TEST_STATE/resources" ;;
  "monitor log-analytics workspace delete --ids "*" --force --yes")
    if [[ "${TEST_FAIL_DELETE:-false}" == true ]]; then exit 1; fi
    if [[ "${TEST_LEAVE_RESOURCE:-false}" == true ]]; then exit 0; fi
    jq 'map(select(.type != "Microsoft.OperationalInsights/workspaces"))' "$TEST_STATE/resources" > "$TEST_STATE/resources.new"
    mv "$TEST_STATE/resources.new" "$TEST_STATE/resources" ;;
  *) echo "Unexpected az invocation: $*" >&2; exit 1 ;;
esac
MOCK
cat > "$test_dir/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$test_dir/az" "$test_dir/sleep"
export PATH="$test_dir:$PATH" TEST_LOG="$test_dir/log" TEST_STATE="$test_dir" \
  AZURE_SUBSCRIPTION_ID=subscription-id AZURE_RESOURCE_GROUP=test-rg AZURE_CONTAINER_REGISTRY=registryname \
  AZURE_SQL_SERVER=sql-server AZURE_STORAGE_ACCOUNT=storageaccount STAFF_CLIENT_ID=staff-client

reset_state() {
  : > "$TEST_LOG"
  echo '{"id":"staff-object","web":{"homePageUrl":"https://existing.example","redirectUris":["https://localhost:7241/signin-oidc","https://staff.example/signin-oidc","https://staff.example/signout-callback-oidc"],"redirectUriSettings":[{"uri":"https://localhost:7241/signin-oidc","index":null},{"uri":"https://staff.example/signin-oidc","index":null},{"uri":"https://staff.example/signout-callback-oidc","index":null}]}}' > "$TEST_STATE/staff"
  jq -n '
    "/subscriptions/subscription-id/resourceGroups/test-rg/providers/" as $prefix |
    {application:"pd-mortgages-demo",managedBy:"bicep"} as $tags |
    [
      {id:($prefix + "Microsoft.ManagedIdentity/userAssignedIdentities/mortgage-demo-env-mortgages"),type:"Microsoft.ManagedIdentity/userAssignedIdentities",name:"mortgage-demo-env-mortgages"},
      {id:($prefix + "Microsoft.ManagedIdentity/userAssignedIdentities/mortgage-demo-env-notifications"),type:"Microsoft.ManagedIdentity/userAssignedIdentities",name:"mortgage-demo-env-notifications"},
      {id:($prefix + "Microsoft.ManagedIdentity/userAssignedIdentities/mortgage-demo-env-notifications-scaler"),type:"Microsoft.ManagedIdentity/userAssignedIdentities",name:"mortgage-demo-env-notifications-scaler"},
      {id:($prefix + "Microsoft.App/containerApps/mortgage-api"),type:"Microsoft.App/containerApps",name:"mortgage-api",tags:$tags},
      {id:($prefix + "Microsoft.App/containerApps/notifications-worker"),type:"Microsoft.App/containerApps",name:"notifications-worker",tags:$tags},
      {id:($prefix + "Microsoft.App/containerApps/staff-web"),type:"Microsoft.App/containerApps",name:"staff-web",tags:$tags},
      {id:($prefix + "Microsoft.App/managedEnvironments/mortgage-demo-env"),type:"Microsoft.App/managedEnvironments",name:"mortgage-demo-env",tags:$tags},
      {id:($prefix + "Microsoft.Network/privateEndpoints/sql-server-private"),type:"Microsoft.Network/privateEndpoints",name:"sql-server-private",tags:$tags},
      {id:($prefix + "Microsoft.Network/networkInterfaces/sql-nic"),type:"Microsoft.Network/networkInterfaces",name:"sql-nic"},
      {id:($prefix + "Microsoft.Sql/servers/sql-server"),type:"Microsoft.Sql/servers",name:"sql-server",tags:$tags},
      {id:($prefix + "Microsoft.Storage/storageAccounts/storageaccount"),type:"Microsoft.Storage/storageAccounts",name:"storageaccount",tags:$tags},
      {id:($prefix + "Microsoft.ServiceBus/namespaces/mortgage-sb-123456789abcd"),type:"Microsoft.ServiceBus/namespaces",name:"mortgage-sb-123456789abcd",tags:$tags},
      {id:($prefix + "Microsoft.ContainerRegistry/registries/registryname"),type:"Microsoft.ContainerRegistry/registries",name:"registryname",tags:$tags},
      {id:($prefix + "Microsoft.Network/virtualNetworks/mortgage-demo-env-vnet"),type:"Microsoft.Network/virtualNetworks",name:"mortgage-demo-env-vnet",tags:$tags},
      {id:($prefix + "Microsoft.Network/privateDnsZones/privatelink.blob.core.windows.net"),type:"Microsoft.Network/privateDnsZones",name:"privatelink.blob.core.windows.net",tags:$tags},
      {id:($prefix + "Microsoft.Network/privateDnsZones/privatelink.database.windows.net"),type:"Microsoft.Network/privateDnsZones",name:"privatelink.database.windows.net",tags:$tags},
      {id:($prefix + "Microsoft.OperationalInsights/workspaces/mortgage-demo-env-logs"),type:"Microsoft.OperationalInsights/workspaces",name:"mortgage-demo-env-logs",tags:$tags}
    ]' > "$TEST_STATE/resources"
  jq -n '
    "/subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.Network/" as $prefix |
    ["privatelink.blob.core.windows.net", "privatelink.database.windows.net"] |
    map({zone: ., name: "demo-vnet", id: ($prefix + "privateDnsZones/" + . + "/virtualNetworkLinks/demo-vnet"),
      virtualNetwork: {id: ($prefix + "virtualNetworks/mortgage-demo-env-vnet")}, registrationEnabled: false})
  ' > "$TEST_STATE/links"
}

reset_state
bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output"
jq -e 'length == 3 and all(.[]; .type == "Microsoft.ManagedIdentity/userAssignedIdentities")' "$TEST_STATE/resources" >/dev/null
jq -e '.web.redirectUris == ["https://localhost:7241/signin-oidc"] and .web.homePageUrl == "https://existing.example"' "$TEST_STATE/staff" >/dev/null
app_line="$(grep -n 'resource delete .*containerApps/mortgage-api' "$TEST_LOG" | cut -d: -f1)"
environment_line="$(grep -n 'resource delete .*managedEnvironments' "$TEST_LOG" | cut -d: -f1)"
endpoint_line="$(grep -n 'resource delete .*privateEndpoints' "$TEST_LOG" | cut -d: -f1)"
network_line="$(grep -n 'resource delete .*virtualNetworks' "$TEST_LOG" | cut -d: -f1)"
link_line="$(grep -n 'network private-dns link vnet delete .*privatelink.blob.core.windows.net' "$TEST_LOG" | cut -d: -f1)"
sql_link_line="$(grep -n 'network private-dns link vnet delete .*privatelink.database.windows.net' "$TEST_LOG" | cut -d: -f1)"
dns_line="$(grep -n 'resource delete .*privateDnsZones/privatelink.blob.core.windows.net' "$TEST_LOG" | cut -d: -f1)"
[[ "$app_line" -lt "$environment_line" && "$environment_line" -lt "$endpoint_line" &&
   "$endpoint_line" -lt "$link_line" && "$link_line" -lt "$network_line" &&
   "$sql_link_line" -lt "$network_line" && "$network_line" -lt "$dns_line" ]]
[[ "$(jq length "$TEST_STATE/links")" == 0 ]]
grep -q 'monitor log-analytics workspace delete .* --force --yes' "$TEST_LOG"
if grep -q 'resource delete .*userAssignedIdentities' "$TEST_LOG"; then
  echo "Deleted a retained identity." >&2; exit 1
fi

: > "$TEST_LOG"
bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output"
if grep -q 'resource delete' "$TEST_LOG"; then echo "Repeated cleanup deleted resources." >&2; exit 1; fi

reset_state
jq 'map(select(.name != "mortgage-demo-env-vnet"))' "$TEST_STATE/resources" > "$TEST_STATE/resources.new"
mv "$TEST_STATE/resources.new" "$TEST_STATE/resources"
jq 'map(select(.zone != "privatelink.blob.core.windows.net"))' "$TEST_STATE/links" > "$TEST_STATE/links.new"
mv "$TEST_STATE/links.new" "$TEST_STATE/links"
bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output"
if grep -q 'link vnet delete .*privatelink.blob.core.windows.net' "$TEST_LOG"; then
  echo "Attempted to delete an already removed DNS link." >&2; exit 1
fi
grep -q 'link vnet delete .*privatelink.database.windows.net' "$TEST_LOG"

for bad_link in extra wrong-vnet registration; do
  reset_state
  case "$bad_link" in
    extra) jq '. + [.[0] | .name = "other" | .id += "-other"]' "$TEST_STATE/links" > "$TEST_STATE/links.new" ;;
    wrong-vnet) jq '.[0].virtualNetwork.id = "/subscriptions/elsewhere"' "$TEST_STATE/links" > "$TEST_STATE/links.new" ;;
    registration) jq '.[0].registrationEnabled = true' "$TEST_STATE/links" > "$TEST_STATE/links.new" ;;
  esac
  mv "$TEST_STATE/links.new" "$TEST_STATE/links"
  if bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output" 2>&1; then
    echo "Expected $bad_link DNS link to block cleanup." >&2; exit 1
  fi
  grep -q 'Unrecognized or invalid virtual network link' "$test_dir/output"
  if grep -Eq 'resource delete|link vnet delete|rest --method patch' "$TEST_LOG"; then
    echo "Changed resources or callbacks despite $bad_link DNS link." >&2; exit 1
  fi
done

reset_state
jq '. + [{id:"/subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.Compute/virtualMachines/other",type:"Microsoft.Compute/virtualMachines",name:"other"}]' \
  "$TEST_STATE/resources" > "$TEST_STATE/resources.new"
mv "$TEST_STATE/resources.new" "$TEST_STATE/resources"
if bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output" 2>&1; then
  echo "Expected unknown resources to block cleanup." >&2; exit 1
fi
grep -q 'Unrecognized resources found; nothing has been deleted' "$test_dir/output"
if grep -Eq 'resource delete|rest --method patch' "$TEST_LOG"; then
  echo "Changed resources or callbacks before unknown-resource check." >&2; exit 1
fi

reset_state
jq 'map(if .name == "mortgage-api" then .id = "/subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.Compute/virtualMachines/other" else . end)' \
  "$TEST_STATE/resources" > "$TEST_STATE/resources.new"
mv "$TEST_STATE/resources.new" "$TEST_STATE/resources"
if bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a mismatched resource ID to block cleanup." >&2; exit 1
fi
if grep -Eq 'resource delete|rest --method patch' "$TEST_LOG"; then
  echo "Changed resources or callbacks despite mismatched ID." >&2; exit 1
fi

reset_state
jq 'map(if .name == "mortgage-api" then del(.tags) else . end)' \
  "$TEST_STATE/resources" > "$TEST_STATE/resources.new"
mv "$TEST_STATE/resources.new" "$TEST_STATE/resources"
if bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output" 2>&1; then
  echo "Expected a missing prototype tag to block cleanup." >&2; exit 1
fi
if grep -Eq 'resource delete|rest --method patch' "$TEST_LOG"; then
  echo "Changed resources or callbacks despite missing tag." >&2; exit 1
fi

reset_state
jq '.web.redirectUriSettings[0].index = 1' "$TEST_STATE/staff" > "$TEST_STATE/staff.new"
mv "$TEST_STATE/staff.new" "$TEST_STATE/staff"
if bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output" 2>&1; then
  echo "Expected indexed redirect settings to block cleanup." >&2; exit 1
fi
if grep -Eq 'resource delete|rest --method patch' "$TEST_LOG"; then
  echo "Changed resources or callbacks despite indexed redirect settings." >&2; exit 1
fi

for failure in TEST_FOREIGN_NIC TEST_FAIL_GRAPH TEST_FAIL_DELETE TEST_FAIL_LINK_LIST TEST_FAIL_LINK_DELETE TEST_LEAVE_RESOURCE TEST_SUBSCRIPTION; do
  reset_state
  if [[ "$failure" == TEST_SUBSCRIPTION ]]; then
    value=other-subscription
  else
    value=true
  fi
  if env "$failure=$value" bash "$root/infra/deployment/delete-prototype.sh" > "$test_dir/output" 2>&1; then
    echo "Expected $failure to fail cleanup." >&2; exit 1
  fi
  if grep -q 'Recognized resources deleted' "$test_dir/output"; then
    echo "Reported success after $failure." >&2; exit 1
  fi
  if [[ "$failure" == TEST_FOREIGN_NIC || "$failure" == TEST_FAIL_GRAPH || "$failure" == TEST_FAIL_LINK_LIST || "$failure" == TEST_SUBSCRIPTION ]] && grep -q 'resource delete' "$TEST_LOG"; then
    echo "Deleted resources after $failure." >&2; exit 1
  fi
  if [[ "$failure" == TEST_FAIL_LINK_DELETE ]] && grep -Eq 'resource delete .*virtualNetworks|resource delete .*privateDnsZones' "$TEST_LOG"; then
    echo "Deleted network or DNS zone after DNS link deletion failed." >&2; exit 1
  fi
done
echo "Prototype deletion mock tests passed."
