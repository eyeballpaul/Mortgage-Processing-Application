#!/usr/bin/env bash
set -euo pipefail

: "${AZURE_RESOURCE_GROUP:?Set AZURE_RESOURCE_GROUP to the dedicated prototype resource group}"
: "${AZURE_CLIENT_ID:?Set AZURE_CLIENT_ID to the GitHub deployment application client ID}"

for command in az jq; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Missing required command: $command" >&2
    exit 1
  fi
done

# Resolve and cross-check the actual Azure scope before changing role assignments.
subscription="$(az account show --query id --output tsv)"
tenant="$(az account show --query tenantId --output tsv)"
group_id="$(az group show --name "$AZURE_RESOURCE_GROUP" --query id --output tsv)"
github_principal="$(az ad sp show --id "$AZURE_CLIENT_ID" --query id --output tsv)"
expected_group="/subscriptions/$subscription/resourceGroups/$AZURE_RESOURCE_GROUP"
if ! jq -en --arg actual "$group_id" --arg expected "$expected_group" \
  '($actual | ascii_downcase) == ($expected | ascii_downcase)' >/dev/null ||
  [[ ! "$github_principal" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  echo "The subscription, group, or deployment identity could not be verified." >&2
  exit 1
fi

# Resolve the five intended principals so the permission condition never uses mutable display names.
identities=()
for suffix in pull mortgages customer-sessions staff; do
  principal="$(az identity show --name "mortgage-demo-env-$suffix" --resource-group "$AZURE_RESOURCE_GROUP" --query principalId --output tsv)"
  if [[ ! "$principal" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    echo "Could not resolve the principal ID for mortgage-demo-env-$suffix." >&2
    exit 1
  fi
  identities+=("$principal")
done

principals=("$github_principal" "${identities[@]}")
unique_principals="$(printf '%s\n' "${principals[@]}" | tr '[:upper:]' '[:lower:]' | sort -u | wc -l | tr -d ' ')"
if [[ "$unique_principals" != "${#principals[@]}" ]]; then
  echo "The deployment and managed identity principals must all be distinct." >&2
  exit 1
fi

# Resolve built-in role IDs by name rather than relying on region- or tenant-specific assumptions.
role_names=(
  "AcrPush"
  "AcrPull"
  "Storage Blob Data Contributor"
  "Azure Service Bus Data Sender"
  "Azure Service Bus Data Receiver"
)
role_ids=()
for role_name in "${role_names[@]}"; do
  role_json="$(az role definition list --name "$role_name" --output json)"
  role_id="$(jq -er --arg name "$role_name" '
    [.[] | select(.roleName == $name)] |
    if length == 1 then .[0].name else error("Expected one built-in role definition") end
  ' <<< "$role_json")"
  if [[ ! "$role_id" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    echo "The built-in role ID for $role_name is invalid." >&2
    exit 1
  fi
  role_ids+=("$role_id")
done

# Limit role-assignment administration to this explicit role/principal matrix.
roles_csv="$(IFS=,; echo "${role_ids[*]}")"
principals_csv="$(IFS=,; echo "${principals[*]}")"
condition="((ActionMatches{'Microsoft.Authorization/roleAssignments/write'} AND @Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roles_csv} AND @Request[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$principals_csv}) OR (ActionMatches{'Microsoft.Authorization/roleAssignments/delete'} AND @Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roles_csv} AND @Resource[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$principals_csv}))"

# Refuse to create a conditioned administrator role unless the expected Contributor grant exists.
assignments="$(az role assignment list --assignee-object-id "$github_principal" --scope "$group_id" --output json)"
if ! jq -e 'type == "array"' <<< "$assignments" >/dev/null; then
  echo "Azure returned an invalid role assignment list." >&2
  exit 1
fi
if ! jq -e --arg scope "$group_id" '
  any(.[]; (.scope | ascii_downcase) == ($scope | ascii_downcase) and
    .roleDefinitionName == "Contributor" and (.condition == null or .condition == ""))
' <<< "$assignments" >/dev/null; then
  echo "The GitHub deployment identity must have an unconditional Contributor assignment on this resource group. Re-run the quickstart or grant it through an Azure administrator." >&2
  exit 1
fi

admin_role=f58310d9-a9f6-439a-9e8d-f62e7b41a168
matching_admin="$(jq --arg scope "$group_id" --arg role "$admin_role" '
  [.[] | select((.scope | ascii_downcase) == ($scope | ascii_downcase) and
    (.roleDefinitionId | ascii_downcase | endswith("/" + $role)))]
' <<< "$assignments")"
case "$(jq 'length' <<< "$matching_admin")" in
  0)
    ;;
  1)
    if ! jq -e --arg condition "$condition" '
      (.[0].condition // "" | gsub("\\s"; "") | ascii_downcase) ==
        ($condition | gsub("\\s"; "") | ascii_downcase) and .[0].conditionVersion == "2.0"
    ' <<< "$matching_admin" >/dev/null; then
      echo "A different group-scoped RBAC administrator condition already exists. It was not changed; review it with an Azure role administrator." >&2
      exit 1
    fi
    echo "The expected group-scoped role-assignment condition is already present."
    exit 0
    ;;
  *)
    echo "Multiple group-scoped RBAC administrator assignments exist for the deployment identity; review them before continuing." >&2
    exit 1
    ;;
esac

echo "Selected tenant: $tenant"
echo "Selected subscription: $subscription"
echo "Selected resource group: $group_id"
echo "GitHub deployment principal: $github_principal"
echo "This grants constrained role-assignment administration on the resource group."
echo "It allows each of five roles for each of five listed principals; inspect the condition before approval."
printf 'Condition: %s\n' "$condition"
read -r -p "Create the conditioned assignment? Type yes: " confirm
if [[ "$confirm" != yes ]]; then
  echo "Setup cancelled."
  exit 1
fi

# Create the least-privilege group-scoped assignment only after explicit administrator confirmation.
az role assignment create \
  --assignee-object-id "$github_principal" \
  --assignee-principal-type ServicePrincipal \
  --role "$admin_role" \
  --scope "$group_id" \
  --condition "$condition" \
  --condition-version 2.0 \
  --output none

# Read back the assignment and verify Azure stored the exact condition before reporting success.
updated="$(az role assignment list --assignee-object-id "$github_principal" --scope "$group_id" --output json)"
if ! jq -e --arg scope "$group_id" --arg role "$admin_role" --arg condition "$condition" '
  any(.[]; (.scope | ascii_downcase) == ($scope | ascii_downcase) and
    (.roleDefinitionId | ascii_downcase | endswith("/" + $role)) and
    (.condition | gsub("\\s"; "") | ascii_downcase) ==
      ($condition | gsub("\\s"; "") | ascii_downcase) and .conditionVersion == "2.0")
' <<< "$updated" >/dev/null; then
  echo "The conditioned assignment could not be verified; inspect Azure IAM before continuing." >&2
  exit 1
fi

echo "The GitHub deployment identity now has the expected group-scoped permissions."
