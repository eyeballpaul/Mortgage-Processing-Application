#!/usr/bin/env bash
set -euo pipefail

: "${AZURE_RESOURCE_GROUP:?Set AZURE_RESOURCE_GROUP to the dedicated prototype resource group}"
: "${AZURE_CLIENT_ID:?Set AZURE_CLIENT_ID to the GitHub deployment application client ID}"

subscription="$(az account show --query id --output tsv)"
group_id="$(az group show --name "$AZURE_RESOURCE_GROUP" --query id --output tsv)"
github_principal="$(az ad sp show --id "$AZURE_CLIENT_ID" --query id --output tsv)"
expected_group="/subscriptions/$subscription/resourceGroups/$AZURE_RESOURCE_GROUP"
if ! jq -en --arg actual "$group_id" --arg expected "$expected_group" \
  '($actual | ascii_downcase) == ($expected | ascii_downcase)' >/dev/null ||
  [[ ! "$github_principal" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  echo "The subscription, group, or deployment identity could not be verified." >&2
  exit 1
fi

admin_role=f58310d9-a9f6-439a-9e8d-f62e7b41a168
assignments="$(az role assignment list --assignee-object-id "$github_principal" --scope "$group_id" --output json)"
assignment="$(jq -ce --arg principal "$github_principal" --arg group "$group_id" --arg role "$admin_role" '
  if type != "array" then error("Invalid role assignment listing") else
    [.[] | select((.principalId | ascii_downcase) == ($principal | ascii_downcase) and
      (.scope | ascii_downcase) == ($group | ascii_downcase) and
      (.roleDefinitionId | ascii_downcase | endswith("/" + $role)))] |
    if length != 1 then error("Expected exactly one group-scoped RBAC administrator assignment")
    elif (.[0].condition | type != "string" or length == 0) or .[0].conditionVersion != "2.0"
      then error("Existing assignment must already have a version 2.0 condition")
    else .[0] end end
' <<< "$assignments")"

echo "Selected group: $group_id"
echo "GitHub deployment principal: $github_principal"
echo "The existing RBAC condition will be preserved and extended only for:"
echo "  Azure Service Bus Data Receiver -> mortgage-demo-env-notifications"
echo "  Azure Service Bus Data Owner -> mortgage-demo-env-notifications-scaler"
echo "Data Owner is broad within a Service Bus namespace; review the setup guide before continuing."
read -r -p "Create the two retained identities if needed and update this condition? Type yes: " confirm
if [[ "$confirm" != yes ]]; then
  echo "Setup cancelled."
  exit 1
fi

identities="$(az identity list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
if ! jq -e 'type == "array"' <<< "$identities" >/dev/null; then
  echo "Invalid managed identity listing." >&2
  exit 1
fi
for name in mortgage-demo-env-notifications mortgage-demo-env-notifications-scaler; do
  count="$(jq --arg name "$name" '[.[] | select(.name == $name)] | length' <<< "$identities")"
  if [[ "$count" == 0 ]]; then
    az identity create --name "$name" --resource-group "$AZURE_RESOURCE_GROUP" --output none
  elif [[ "$count" != 1 ]]; then
    echo "Managed identity name $name is ambiguous." >&2
    exit 1
  fi
done
worker="$(az identity show --name mortgage-demo-env-notifications --resource-group "$AZURE_RESOURCE_GROUP" --query principalId --output tsv)"
scaler="$(az identity show --name mortgage-demo-env-notifications-scaler --resource-group "$AZURE_RESOURCE_GROUP" --query principalId --output tsv)"
for principal in "$worker" "$scaler"; do
  if [[ ! "$principal" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    echo "A notification identity has an invalid principal ID." >&2
    exit 1
  fi
done
if [[ "$worker" == "$scaler" || "$worker" == "$github_principal" || "$scaler" == "$github_principal" ]]; then
  echo "Notification and deployment principals must be distinct." >&2
  exit 1
fi

receiver=4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0
owner=090c5cfd-751d-490a-894a-3ce6f1109419
extra="((ActionMatches{'Microsoft.Authorization/roleAssignments/write'} AND ((@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$receiver} AND @Request[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$worker}) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$owner} AND @Request[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$scaler}))) OR (ActionMatches{'Microsoft.Authorization/roleAssignments/delete'} AND ((@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$receiver} AND @Resource[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$worker}) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$owner} AND @Resource[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$scaler}))))"
old_condition="$(jq -er '.condition' <<< "$assignment")"
if jq -en --arg old "$old_condition" --arg extra "$extra" '
  ($old | gsub("\\s"; "") | ascii_downcase) |
  contains($extra | gsub("\\s"; "") | ascii_downcase)
' >/dev/null; then
  echo "The notification permission extension is already present."
  exit 0
fi
condition="($old_condition) OR ($extra)"
assignment_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$assignment")"
current="$(az role assignment list --assignee-object-id "$github_principal" --scope "$group_id" --output json)"
if ! jq -e --argjson before "$assignment" '
  [.[] | select(.id == $before.id)] |
  length == 1 and .[0].principalId == $before.principalId and
  .[0].scope == $before.scope and .[0].roleDefinitionId == $before.roleDefinitionId and
  .[0].condition == $before.condition and .[0].conditionVersion == $before.conditionVersion and
  .[0].description == $before.description
' <<< "$current" >/dev/null; then
  echo "The existing role assignment changed during setup; inspect it before retrying." >&2
  exit 1
fi
parameter_file="$(mktemp "${TMPDIR:-/tmp}/mortgage-rbac.XXXXXXXX")"
trap 'rm -f -- "$parameter_file"' EXIT
chmod 600 "$parameter_file"
jq --arg condition "$condition" '.condition = $condition' <<< "$assignment" > "$parameter_file"
az role assignment update --role-assignment "$parameter_file" --output none
updated="$(az role assignment list --assignee-object-id "$github_principal" --scope "$group_id" --output json)"
if ! jq -e --arg id "$assignment_id" --arg condition "$condition" '
  any(.[]; .id == $id and
    (.condition | gsub("\\s"; "") | ascii_downcase) == ($condition | gsub("\\s"; "") | ascii_downcase) and
    .conditionVersion == "2.0")
' <<< "$updated" >/dev/null; then
  echo "The updated role condition could not be verified; inspect it in Azure before deploying." >&2
  exit 1
fi
echo "Notification identities and restricted deployment permissions are ready."
echo "Review other role assignments separately; existing permissions were not narrowed."
