#!/usr/bin/env bash
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
: "${AZURE_SQL_SERVER:?Missing AZURE_SQL_SERVER}"
: "${AZURE_STORAGE_ACCOUNT:?Missing AZURE_STORAGE_ACCOUNT}"
: "${STAFF_CLIENT_ID:?Missing STAFF_CLIENT_ID}"

cleanup_failure() {
  local status=$?
  if [[ "$status" != 0 ]]; then
    echo "Cleanup failed or stopped. Some billable resources may remain; review the error and retry after correcting it." >&2
  fi
}
trap cleanup_failure EXIT

subscription="$(az account show --query id --output tsv)"
if [[ "$subscription" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "The selected subscription does not match AZURE_SUBSCRIPTION_ID." >&2
  exit 1
fi
if [[ "$(az group exists --name "$AZURE_RESOURCE_GROUP" --output tsv)" != true ]]; then
  echo "The persistent resource group is missing; nothing has been deleted." >&2
  exit 1
fi
group_id="$(az group show --name "$AZURE_RESOURCE_GROUP" --query id --output tsv)"
expected_group="/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$AZURE_RESOURCE_GROUP"
if ! jq -en --arg actual "$group_id" --arg expected "$expected_group" \
  '($actual | ascii_downcase) == ($expected | ascii_downcase)' >/dev/null; then
  echo "Resource group ID does not match the configured subscription and group." >&2
  exit 1
fi

classify() {
  jq -ce --arg group "$group_id" --arg registry "$AZURE_CONTAINER_REGISTRY" \
    --arg sql "$AZURE_SQL_SERVER" --arg storage "$AZURE_STORAGE_ACCOUNT" '
    def tagged: (.tags.application == "pd-mortgages-demo" and .tags.managedBy == "bicep");
    def oneof($values): . as $v | any($values[]; ascii_downcase == ($v | ascii_downcase));
    def classify:
      (.type | ascii_downcase) as $type |
      (.name | ascii_downcase) as $name |
      if $type == "microsoft.managedidentity/userassignedidentities" and
        ($name | oneof(["mortgage-demo-env-pull", "mortgage-demo-env-mortgages", "mortgage-demo-env-products",
          "mortgage-demo-env-customer-sessions", "mortgage-demo-env-bootstrap", "mortgage-demo-env-staff"])) then "retain"
      elif $type == "microsoft.insights/scheduledqueryrules" and $name == "mortgage-demo-env-server-errors" and tagged then "alert-rules"
      elif $type == "microsoft.insights/actiongroups" and $name == "mortgage-demo-env-alerts" and tagged then "alert-groups"
      elif $type == "microsoft.app/containerapps" and
        ($name | oneof(["products-api", "eligibility-api", "mortgage-api", "staff-web", "mortgages-web"])) and tagged then "apps"
      elif $type == "microsoft.app/jobs" and $name == "mortgage-demo-env-bootstrap" and tagged then "apps"
      elif $type == "microsoft.app/managedenvironments" and $name == "mortgage-demo-env" and tagged then "environment"
      elif $type == "microsoft.network/privateendpoints" and
        ($name | oneof([($sql + "-private"), ($storage + "-private")])) and tagged then "endpoints"
      elif $type == "microsoft.sql/servers" and ($name == ($sql | ascii_downcase)) and tagged then "data"
      elif $type == "microsoft.storage/storageaccounts" and ($name == ($storage | ascii_downcase)) and tagged then "data"
      elif $type == "microsoft.servicebus/namespaces" and ($name | test("^mortgage-sb-[a-z0-9]{13}$")) and tagged then "data"
      elif $type == "microsoft.containerregistry/registries" and ($name == ($registry | ascii_downcase)) and tagged then "data"
      elif $type == "microsoft.network/virtualnetworks" and $name == "mortgage-demo-env-vnet" and tagged then "network"
      elif $type == "microsoft.network/privatednszones" and
        ($name | oneof(["privatelink.database.windows.net", "privatelink.blob.core.windows.net"])) and tagged then "dns"
      elif $type == "microsoft.operationalinsights/workspaces" and $name == "mortgage-demo-env-logs" and tagged then "logs"
      elif $type == "microsoft.network/networkinterfaces" then "nic"
      elif $type == "microsoft.sql/servers/databases" and
        ($name | oneof([($sql + "/master"), ($sql + "/mortgages"), ($sql + "/products"), ($sql + "/customer-sessions")])) then "child"
      elif $type == "microsoft.network/virtualnetworks/subnets" and
        ($name | oneof(["mortgage-demo-env-vnet/container-apps", "mortgage-demo-env-vnet/private-endpoints"])) then "child"
      elif $type == "microsoft.network/privatednszones/virtualnetworklinks" and
        ($name | oneof(["privatelink.database.windows.net/demo-vnet", "privatelink.blob.core.windows.net/demo-vnet"])) then "child"
      elif $type == "microsoft.network/privateendpoints/privatednszonegroups" and
        ($name | oneof([($sql + "-private/default"), ($storage + "-private/default")])) then "child"
      elif $type == "microsoft.storage/storageaccounts/blobservices" and $name == (($storage + "/default") | ascii_downcase) then "child"
      elif $type == "microsoft.storage/storageaccounts/blobservices/containers" and
        ($name | oneof([($storage + "/default/mortgage-documents"), ($storage + "/default/data-protection"), ($storage + "/default/staff-data-protection")])) then "child"
      elif $type == "microsoft.servicebus/namespaces/queues" and
        ($name | test("^mortgage-sb-[a-z0-9]{13}/submission-notifications$")) then "child"
      else "unknown" end;
    if type != "array" then error("Resource listing is not an array") else
      map(if (.id | type != "string") or (.name | type != "string") or (.type | type != "string") or
        ((.id | ascii_downcase | startswith(($group | ascii_downcase) + "/providers/")) | not)
        then error("Invalid or out-of-scope resource in listing")
        else . + {category: classify} |
          if .category != "child" and .category != "unknown" and
            ((.id | ascii_downcase) != (($group + "/providers/" + .type + "/" + .name) | ascii_downcase))
          then error("Resource ID does not match its type and name") else . end
        end)
    end'
}

resources="$(az resource list --resource-group "$AZURE_RESOURCE_GROUP" --output json | classify)"
if jq -e 'any(.[]; .category == "unknown")' <<< "$resources" >/dev/null; then
  echo "Unrecognized resources found; nothing has been deleted. Review these resources and their costs:" >&2
  jq -r '.[] | select(.category == "unknown") | .id' <<< "$resources" >&2
  exit 1
fi

# Private endpoints create untagged network interfaces, which Azure deletes with their endpoints.
while IFS= read -r nic_id; do
  endpoint="$(az network nic show --ids "$nic_id" --query privateEndpoint.id --output tsv)"
  if ! jq -en --arg endpoint "$endpoint" --arg group "$group_id" --arg sql "$AZURE_SQL_SERVER" --arg storage "$AZURE_STORAGE_ACCOUNT" '
    ($endpoint | ascii_downcase) as $actual |
    [($group + "/providers/Microsoft.Network/privateEndpoints/" + $sql + "-private"),
     ($group + "/providers/Microsoft.Network/privateEndpoints/" + $storage + "-private")] |
    any(.[]; (ascii_downcase) == $actual)
  ' >/dev/null; then
    echo "Unrecognized network interface $nic_id; nothing has been deleted." >&2
    exit 1
  fi
done < <(jq -r '.[] | select(.category == "nic") | .id' <<< "$resources")

dns_links='[]'
while IFS= read -r zone_name; do
  links="$(az network private-dns link vnet list --resource-group "$AZURE_RESOURCE_GROUP" \
    --zone-name "$zone_name" --output json)"
  if ! jq -e --arg zone "$group_id/providers/Microsoft.Network/privateDnsZones/$zone_name" \
    --arg vnet "$group_id/providers/Microsoft.Network/virtualNetworks/mortgage-demo-env-vnet" '
      type == "array" and all(.[];
        (.id | type == "string" and ascii_downcase == (($zone + "/virtualNetworkLinks/demo-vnet") | ascii_downcase)) and
        (.name | type == "string" and ascii_downcase == "demo-vnet") and
        (.virtualNetwork.id | type == "string" and ascii_downcase == ($vnet | ascii_downcase)) and
        .registrationEnabled == false)
    ' <<< "$links" >/dev/null; then
    echo "Unrecognized or invalid virtual network link in DNS zone $zone_name; nothing has been deleted." >&2
    exit 1
  fi
  dns_links="$(jq -cn --argjson existing "$dns_links" --argjson links "$links" '$existing + $links')"
done < <(jq -r '.[] | select(.category == "dns") | .name' <<< "$resources")

if jq -e 'any(.[]; .category == "apps" and (.type | ascii_downcase) == "microsoft.app/containerapps" and (.name | ascii_downcase) == "staff-web")' <<< "$resources" >/dev/null; then
  staff_container="$(az containerapp show --name staff-web --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  if ! jq -e --arg client "$STAFF_CLIENT_ID" '
    any(.properties.template.containers[0].env[]?; .name == "AzureAd__ClientId" and .value == $client)
  ' <<< "$staff_container" >/dev/null; then
    echo "The deployed staff app does not match STAFF_CLIENT_ID; no resources were deleted." >&2
    exit 1
  fi
  staff_fqdn="$(jq -er '.properties.configuration.ingress.fqdn | select(type == "string" and length > 0)' <<< "$staff_container")"
  if [[ ! "$staff_fqdn" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]; then
    echo "The deployed staff app returned an invalid hostname; no resources were deleted." >&2
    exit 1
  fi
  staff_app="$(az ad app show --id "$STAFF_CLIENT_ID" --output json)"
  staff_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$staff_app")"
  signin="https://$staff_fqdn/signin-oidc"
  signout="https://$staff_fqdn/signout-callback-oidc"
  staff_update="$(jq -ce --arg signin "$signin" --arg signout "$signout" '
    (.web.redirectUris // []) as $uris |
    if ($uris | type == "array" and all(.[]; type == "string")) and
       ((.web.redirectUriSettings // []) | type == "array" and all(.[]; .index == null)) then
      {web: (((.web // {}) | del(.redirectUriSettings)) + {redirectUris:($uris | map(select(. != $signin and . != $signout)))})}
    else error("Invalid or indexed staff redirect settings") end
  ' <<< "$staff_app")"
  if jq -e --arg signin "$signin" --arg signout "$signout" '
    (.web.redirectUris // []) as $uris | ($uris | index($signin)) != null or ($uris | index($signout)) != null
  ' <<< "$staff_app" >/dev/null; then
    az rest --method patch --url "https://graph.microsoft.com/v1.0/applications/$staff_id" --body "$staff_update" --output none
    callbacks_verified=false
    for ((attempt=1; attempt<=6; attempt++)); do
      staff_after="$(az ad app show --id "$STAFF_CLIENT_ID" --output json)"
      if jq -e --argjson before "$staff_app" --arg signin "$signin" --arg signout "$signout" '
        .web as $web | (.web.redirectUris // []) as $uris |
        ($uris | index($signin)) == null and ($uris | index($signout)) == null and
        all(($before.web.redirectUris // [])[] | select(. != $signin and . != $signout);
          . as $old | ($uris | index($old)) != null) and
        all((($before.web // {}) | to_entries[] | select(.key != "redirectUris" and .key != "redirectUriSettings")); . as $entry | $web[$entry.key] == $entry.value)
      ' <<< "$staff_after" >/dev/null; then
        callbacks_verified=true
        break
      fi
      sleep 5
    done
    if [[ "$callbacks_verified" != true ]]; then
      echo "Staff callback removal could not be verified; inspect the registration. No resources were deleted." >&2
      exit 1
    fi
  fi
fi

echo "Deleting recognized prototype resources from $group_id. Databases, blobs and queue data will be lost."
for category in alert-rules alert-groups apps environment endpoints data dns-links network dns logs; do
  if [[ "$category" == dns-links ]]; then
    while IFS= read -r id; do
      echo "Deleting $id"
      az network private-dns link vnet delete --ids "$id" --yes
    done < <(jq -r '.[].id' <<< "$dns_links")
    continue
  fi
  while IFS= read -r id; do
    echo "Deleting $id"
    if [[ "$category" == logs ]]; then
      az monitor log-analytics workspace delete --ids "$id" --force --yes
    else
      az resource delete --ids "$id"
    fi
  done < <(jq -r --arg category "$category" '.[] | select(.category == $category) | .id' <<< "$resources")
done

remaining="$(az resource list --resource-group "$AZURE_RESOURCE_GROUP" --output json | classify)"
if jq -e 'any(.[]; .category != "retain")' <<< "$remaining" >/dev/null; then
  echo "Resources remain after cleanup; inspect them and their costs before retrying:" >&2
  jq -r '.[] | select(.category != "retain") | .id' <<< "$remaining" >&2
  exit 1
fi
echo "Recognized resources deleted. The resource group and managed identities remain."
echo "Check Cost Management for delayed charges, soft-deleted data, and resources outside this group."
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  printf '## Prototype resources deleted\n\nResource group and managed identities retained. Check Cost Management for remaining charges and resources outside the group.\n' >> "$GITHUB_STEP_SUMMARY"
fi
