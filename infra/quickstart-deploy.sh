#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

for command in az jq openssl curl; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Missing required command: $command" >&2
    exit 1
  fi
done

export AZURE_EXTENSION_USE_DYNAMIC_INSTALL=no
if ! az containerapp job execution show --help >/dev/null; then
  echo "Azure CLI Container Apps commands are unavailable. Update Azure CLI or install the containerapp extension." >&2
  exit 1
fi

if ! az account show --output none; then
  echo "Sign in with 'az login' and select your subscription with 'az account set' before running this script." >&2
  exit 1
fi

automated="${AUTOMATED_DEPLOYMENT:-false}"
if [[ "$automated" == true ]]; then
  : "${AZURE_CLIENT_ID:?Missing AZURE_CLIENT_ID}"
  : "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
  : "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
  : "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
  : "${AZURE_SQL_SERVER:?Missing AZURE_SQL_SERVER}"
  : "${AZURE_STORAGE_ACCOUNT:?Missing AZURE_STORAGE_ACCOUNT}"
  : "${MORTGAGE_API_CLIENT_ID:?Missing MORTGAGE_API_CLIENT_ID}"
  : "${STAFF_CLIENT_ID:?Missing STAFF_CLIENT_ID}"
  : "${STAFF_CLIENT_SECRET:?Missing STAFF_CLIENT_SECRET}"
  : "${PRODUCTS_API_KEY:?Missing PRODUCTS_API_KEY}"
  : "${ELIGIBILITY_API_KEY:?Missing ELIGIBILITY_API_KEY}"
  principal_id="$(az ad sp show --id "$AZURE_CLIENT_ID" --query id --output tsv)"
  principal_type=ServicePrincipal
else
  principal_id="$(az ad signed-in-user show --query id --output tsv)"
  principal_type=User
fi
if [[ -z "$principal_id" || "$principal_id" == "None" ]]; then
  echo "Could not resolve the signed-in deployment principal." >&2
  exit 1
fi
subscription_name="$(az account show --query name --output tsv)"
subscription_id="$(az account show --query id --output tsv)"
tenant_id="$(az account show --query tenantId --output tsv)"
echo "Selected subscription: $subscription_name ($subscription_id)"
echo "This deployment creates billable resources. An Azure budget does not cap spending."
echo "Azure SQL free-offer databases become unavailable for the rest of the month when their free compute is exhausted."
if [[ "$automated" == true ]]; then
  echo "This script does not pause resources. Run the delete workflow when finished."
else
  echo "This script does not pause resources. Delete the resource group when finished if you do not need its data."
fi

if [[ "$automated" == true ]]; then
  if [[ "$subscription_id" != "$AZURE_SUBSCRIPTION_ID" ]]; then
    echo "The selected Azure subscription does not match AZURE_SUBSCRIPTION_ID." >&2
    exit 1
  fi
  resource_group="$AZURE_RESOURCE_GROUP"
else
  read -r -p "Resource group name (existing or new): " resource_group
fi
if [[ -z "$resource_group" ]]; then
  echo "A resource group name is required." >&2
  exit 1
fi
group_exists="$(az group exists --name "$resource_group" --output tsv)"
case "$group_exists" in
  true) location="$(az group show --name "$resource_group" --query location --output tsv)" ;;
  false)
    if [[ "$automated" == true ]]; then
      echo "The persistent resource group is missing; complete one-time setup before running the workflow." >&2
      exit 1
    fi
    read -r -p "Region for the new resource group [uksouth]: " location
    location="${location:-uksouth}" ;;
  *) echo "Could not determine whether the resource group exists." >&2; exit 1 ;;
esac
if [[ "$automated" == true ]]; then
  registry="$AZURE_CONTAINER_REGISTRY"
  sql_server="$AZURE_SQL_SERVER"
  storage="$AZURE_STORAGE_ACCOUNT"
  api_client_id="$MORTGAGE_API_CLIENT_ID"
  staff_client_id="$STAFF_CLIENT_ID"
  staff_secret="$STAFF_CLIENT_SECRET"
  products_key="$PRODUCTS_API_KEY"
  eligibility_key="$ELIGIBILITY_API_KEY"
  unset STAFF_CLIENT_SECRET PRODUCTS_API_KEY ELIGIBILITY_API_KEY
else
  suffix="$(openssl rand -hex 4)"
  read -r -p "Container registry name [mortgagedemo$suffix]: " registry
  registry="${registry:-mortgagedemo$suffix}"
  read -r -p "SQL server name [mortgage-demo-sql-$suffix]: " sql_server
  sql_server="${sql_server:-mortgage-demo-sql-$suffix}"
  read -r -p "Storage account name [mortgagest$suffix]: " storage
  storage="${storage:-mortgagest$suffix}"
  read -r -p "Mortgage API application (client) ID: " api_client_id
  read -r -p "Staff web application (client) ID: " staff_client_id
  read -r -s -p "Staff web client secret value: " staff_secret
  echo
  echo "Enter two different API keys (32-128 letters, digits, underscores or hyphens)."
  echo "Generate each with 'openssl rand -hex 32' and retain them securely for retries."
  read -r -s -p "Products API key: " products_key
  echo
  read -r -s -p "Eligibility API key: " eligibility_key
  echo
fi

if [[ -z "$api_client_id" || -z "$staff_client_id" || -z "$staff_secret" ]]; then
  echo "Both client IDs and the staff secret are required." >&2
  exit 1
fi
if [[ ! "$products_key" =~ ^[A-Za-z0-9_-]{32,128}$ || ! "$eligibility_key" =~ ^[A-Za-z0-9_-]{32,128}$ || "$products_key" == "$eligibility_key" ]]; then
  echo "The API keys must be different and each contain 32-128 permitted characters." >&2
  exit 1
fi

if [[ "$automated" == true ]]; then
  configure_github=preconfigured
else
  read -r -p "Configure the existing GitHub deployment identity and repository variables? [Y/n]: " configure_github
  case "$configure_github" in
    ""|y|Y|yes|YES) configure_github=true ;;
    n|N|no|NO) configure_github=false ;;
    *) echo "Answer yes or no." >&2; exit 1 ;;
  esac
fi
if [[ "$configure_github" == true ]]; then
  if ! command -v gh >/dev/null 2>&1; then
    echo "GitHub CLI (gh) is required for automatic GitHub setup. Install and sign in, or rerun and choose no for manual setup." >&2
    exit 1
  fi
  export GH_HOST=github.com
  gh auth status --hostname github.com
  read -r -p "GitHub repository (owner/name): " github_repo
  if [[ ! "$github_repo" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
    echo "Enter a GitHub repository as owner/name." >&2
    exit 1
  fi
  github_repo="$(gh repo view "$github_repo" --json nameWithOwner --jq .nameWithOwner)"
  github_client_id="$(gh variable get AZURE_CLIENT_ID --repo "$github_repo")"
  github_tenant_id="$(gh variable get AZURE_TENANT_ID --repo "$github_repo")"
  if [[ -z "$github_client_id" || "$github_tenant_id" != "$tenant_id" ]]; then
    echo "The repository must have an existing AZURE_CLIENT_ID and an AZURE_TENANT_ID matching the selected Azure tenant." >&2
    exit 1
  fi
  github_principal_id="$(az ad sp show --id "$github_client_id" --query id --output tsv)"
  if [[ -z "$github_principal_id" || "$github_principal_id" == None ]]; then
    echo "The GitHub deployment service principal was not found in the selected tenant." >&2
    exit 1
  fi
fi

staff_app="$(az ad app show --id "$staff_client_id" --output json)"
staff_object_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$staff_app")"
if ! jq -e '
  ((.web.redirectUris // []) | type == "array" and all(.[]; type == "string")) and
  ((.web.redirectUriSettings // []) | type == "array" and all(.[]; .index == null))
' <<< "$staff_app" >/dev/null; then
  echo "The staff registration has invalid or indexed web redirect settings; review it before deployment." >&2
  exit 1
fi
api_sp="$(az ad sp show --id "$api_client_id" --output json)"
api_principal_id="$(jq -er '.id' <<< "$api_sp")"
customer_role_id="$(jq -er '[.appRoles[] | select(.value == "MortgageCustomer" and .isEnabled and (.allowedMemberTypes | index("Application")))] | if length == 1 then .[0].id else error("MortgageCustomer application role missing or ambiguous") end' <<< "$api_sp")"

if [[ "$automated" == true ]]; then
  for identity in pull mortgages products customer-sessions bootstrap staff notifications notifications-scaler; do
    if ! identity_json="$(az identity show --name "mortgage-demo-env-$identity" --resource-group "$resource_group" --output json)"; then
      echo "Required retained identity mortgage-demo-env-$identity is unavailable; complete one-time setup before deployment." >&2
      exit 1
    fi
    jq -e '.principalId | type == "string" and length > 0' <<< "$identity_json" >/dev/null
    if [[ "$identity" == customer-sessions ]]; then
      expected_customer_principal="$(jq -er '.principalId' <<< "$identity_json")"
      expected_customer_client="$(jq -er '.clientId' <<< "$identity_json")"
    fi
  done
  assignment_url="https://graph.microsoft.com/v1.0/servicePrincipals/$expected_customer_principal/appRoleAssignments"
  customer_assignments="$(az rest --method get --url "$assignment_url" --output json)"
  if ! jq -e '.value | type == "array"' <<< "$customer_assignments" >/dev/null; then
    echo "Microsoft Graph returned an invalid customer app-role assignment list." >&2
    exit 1
  fi
  if ! jq -e --arg resource "$api_principal_id" --arg role "$customer_role_id" \
      'any(.value[]; .resourceId == $resource and .appRoleId == $role)' <<< "$customer_assignments" >/dev/null; then
    if jq -e '."@odata.nextLink" != null' <<< "$customer_assignments" >/dev/null; then
      echo "Customer app-role results are paginated; verify the MortgageCustomer assignment before deployment." >&2
      exit 1
    fi
    echo "The retained customer identity lacks its MortgageCustomer app role; complete one-time setup before deployment." >&2
    exit 1
  fi
fi

if [[ "$group_exists" == true ]]; then
  existing_apps="$(az containerapp list --resource-group "$resource_group" --output json)"
  if ! jq -e 'type == "array"' <<< "$existing_apps" >/dev/null; then
    echo "Could not verify existing Container Apps; refusing deployment." >&2
    exit 1
  fi
  if jq -e 'any(.[]; .name == "mortgage-api")' <<< "$existing_apps" >/dev/null &&
     ! jq -e 'any(.[]; .name == "mortgage-api" and
       any(.properties.template.containers[0].env[]?; .name == "Notifications__WorkerModel" and .value == "separate")) and
       any(.[]; .name == "notifications-worker")' <<< "$existing_apps" >/dev/null; then
    echo "An older notification consumer is still deployed in mortgage-api. Deployment stopped before making changes." >&2
    echo "Back up any required data and follow the legacy cutover instructions before deploying the separate worker." >&2
    exit 1
  fi
fi

echo "Resource group: $resource_group ($location; exists: $group_exists)"
echo "Registry: $registry; SQL server: $sql_server; storage: $storage"
echo "Staff app registration $staff_client_id will receive the deployed sign-in and sign-out callback URIs."
if [[ "$configure_github" == true ]]; then
  echo "GitHub repository: https://github.com/$github_repo; existing deployment client: $github_client_id"
  echo "Its Contributor role will be granted on the resource group, and constrained role-assignment access on the notification queue."
  echo "Repository subscription, resource-group and registry variables will be updated after deployment."
elif [[ "$automated" != true ]]; then
  echo "GitHub deployment permissions and variables must be configured manually."
fi
echo "Existing resources and app settings may be overwritten. Do not run this alongside a GitHub deployment."
echo "The deployment principal needs deployment and role-assignment permissions and Entra app-management permissions."
if [[ "$automated" != true ]]; then
  read -r -p "Create or update these Azure resources? Type yes to continue: " confirm
  if [[ "$confirm" != "yes" ]]; then
    echo "Deployment cancelled."
    exit 1
  fi
fi

cleanup() {
  local status=$?
  if [[ -n "${parameter_file:-}" ]]; then rm -f -- "$parameter_file"; fi
  if [[ "$status" != 0 ]]; then
    echo "Quickstart failed. Partial resources or configuration may remain and incur charges; correct the error and retry with the same names and secrets." >&2
  fi
}
trap cleanup EXIT

if [[ "$group_exists" == false ]]; then
  echo "Creating resource group..."
  az group create --name "$resource_group" --location "$location" --output none
fi

parameter_file="$(mktemp "${TMPDIR:-/tmp}/mortgage-parameters.XXXXXXXX")"
chmod 600 "$parameter_file"

export location registry sql_server storage api_client_id staff_client_id staff_secret principal_id principal_type products_key eligibility_key
jq -n '{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  contentVersion: "1.0.0.0",
  parameters: {
    location: {value: env.location},
    containerRegistryName: {value: env.registry},
    containerAppEnvironmentName: {value: "mortgage-demo-env"},
    sqlServerName: {value: env.sql_server},
    documentStorageAccountName: {value: env.storage},
    mortgagesWebAppName: {value: "mortgages-web"},
    staffWebAppName: {value: "staff-web"},
    mortgageApiAppName: {value: "mortgage-api"},
    productsApiAppName: {value: "products-api"},
    eligibilityApiAppName: {value: "eligibility-api"},
    mortgageApiClientId: {value: env.api_client_id},
    staffClientId: {value: env.staff_client_id},
    staffClientSecret: {value: env.staff_secret},
    pipelinePrincipalId: {value: env.principal_id},
    pipelinePrincipalType: {value: env.principal_type},
    productServiceApiKey: {value: env.products_key},
    eligibilityServiceApiKey: {value: env.eligibility_key},
    imageTag: {value: "latest"}
  }
}' > "$parameter_file"
unset staff_secret products_key eligibility_key

deploy() {
  az deployment group create --name "$1" --resource-group "$resource_group" \
    --template-file infra/main.bicep --parameters "@$parameter_file" \
    --parameters "deployApps=$2" "deployBootstrap=$3" "createSqlServer=$4" --output none
}

servers="$(az sql server list --resource-group "$resource_group" --output json)"
if jq -e --arg name "$sql_server" 'any(.[]; .name == $name)' <<< "$servers" >/dev/null; then
  create_sql_server=false
  echo "Reusing existing SQL server: $sql_server"
else
  create_sql_server=true
fi

echo "Deploying foundation..."
deploy mortgage-foundation false false "$create_sql_server"
customer_principal="$(az deployment group show --name mortgage-foundation --resource-group "$resource_group" \
  --query properties.outputs.customerSessionsIdentityPrincipalId.value --output tsv)"
customer_client="$(az deployment group show --name mortgage-foundation --resource-group "$resource_group" \
  --query properties.outputs.customerSessionsIdentityClientId.value --output tsv)"
if [[ -z "$customer_principal" || "$customer_principal" == "None" || -z "$customer_client" || "$customer_client" == "None" ]]; then
  echo "Foundation deployment did not return the customer managed identity." >&2
  exit 1
fi
if [[ "$automated" == true && ( "$customer_principal" != "$expected_customer_principal" || "$customer_client" != "$expected_customer_client" ) ]]; then
  echo "The customer managed identity changed during deployment; refusing to continue." >&2
  exit 1
fi

mortgage_principal="$(az deployment group show --name mortgage-foundation --resource-group "$resource_group" \
  --query properties.outputs.mortgagesIdentityPrincipalId.value --output tsv)"
queue_id="$(az deployment group show --name mortgage-foundation --resource-group "$resource_group" \
  --query properties.outputs.notificationQueueId.value --output tsv)"
bash infra/deployment/remove-legacy-receiver.sh "$mortgage_principal" "$queue_id"

if [[ "$configure_github" == true ]]; then
  if [[ ! "$mortgage_principal" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] ||
    ! jq -en --arg queue "$queue_id" --arg prefix "/subscriptions/$subscription_id/resourceGroups/$resource_group/providers/Microsoft.ServiceBus/namespaces/" \
      '($queue | ascii_downcase) as $q | ($q | startswith($prefix | ascii_downcase)) and ($q | endswith("/queues/submission-notifications"))' >/dev/null; then
    echo "Foundation deployment did not return the expected mortgage identity and notification queue." >&2
    exit 1
  fi
  group_scope="/subscriptions/$subscription_id/resourceGroups/$resource_group"
  assignments="$(az role assignment list --assignee "$github_principal_id" --scope "$group_scope" --output json)"
  if ! jq -e --arg scope "$group_scope" 'any(.[]; (.scope | ascii_downcase) == ($scope | ascii_downcase) and .roleDefinitionName == "Contributor" and (.condition == null or .condition == ""))' <<< "$assignments" >/dev/null; then
    az role assignment create --assignee-object-id "$github_principal_id" --assignee-principal-type ServicePrincipal \
      --role Contributor --scope "$group_scope" --output none
  fi
  sender=69a216fc-b8fb-44d8-bc22-1f3c2cd27a39
  receiver=4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0
  admin_role=f58310d9-a9f6-439a-9e8d-f62e7b41a168
  condition="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$sender, $receiver} AND @Request[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$mortgage_principal})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$sender, $receiver} AND @Resource[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$mortgage_principal}))"
  queue_assignments="$(az role assignment list --assignee "$github_principal_id" --scope "$queue_id" --output json)"
  matching_admin="$(jq --arg scope "$queue_id" --arg role "$admin_role" \
    '[.[] | select((.scope | ascii_downcase) == ($scope | ascii_downcase) and (.roleDefinitionId | ascii_downcase | endswith("/" + $role)))]' <<< "$queue_assignments")"
  if [[ "$(jq 'length' <<< "$matching_admin")" == 0 ]]; then
    az role assignment create --assignee-object-id "$github_principal_id" --assignee-principal-type ServicePrincipal \
      --role "$admin_role" --scope "$queue_id" --condition "$condition" --condition-version 2.0 --output none
  elif ! jq -e --arg condition "$condition" 'any(.[];
    (.condition // "" | gsub("\\s"; "") | ascii_downcase) == ($condition | gsub("\\s"; "") | ascii_downcase) and
    .conditionVersion == "2.0")' <<< "$matching_admin" >/dev/null; then
    echo "An existing queue-scoped RBAC administrator assignment has a different condition. It was not changed; review it before retrying." >&2
    exit 1
  fi
  if [[ "$(jq 'length' <<< "$matching_admin")" -gt 1 ]]; then
    echo "Multiple queue-scoped RBAC administrator assignments exist; review other assignments for broader permissions." >&2
  fi
fi

if [[ "$automated" != true ]]; then
  identity_available=false
  for ((attempt=1; attempt<=12; attempt++)); do
    if az ad sp show --id "$customer_client" --output none; then
      identity_available=true
      break
    fi
    sleep 10
  done
  if [[ "$identity_available" != true ]]; then
    echo "The customer managed identity is not available in Entra. Verify directory permissions before retrying." >&2
    exit 1
  fi

  assignment_url="https://graph.microsoft.com/v1.0/servicePrincipals/$customer_principal/appRoleAssignments"
  existing_assignment="$(az rest --method get --url "$assignment_url" --output json |
    jq -r --arg resource "$api_principal_id" --arg role "$customer_role_id" \
      '[.value[] | select(.resourceId == $resource and .appRoleId == $role)] | length')"
  if [[ "$existing_assignment" == "0" ]]; then
    assignment_body="$(jq -n --arg principal "$customer_principal" --arg resource "$api_principal_id" \
      --arg role "$customer_role_id" '{principalId:$principal,resourceId:$resource,appRoleId:$role}')"
    az rest --method post --url "$assignment_url" --body "$assignment_body" --output none
  fi
fi

echo "Building images in Azure Container Registry..."
for image_and_dockerfile in \
  "database-bootstrap:PD.Mortgages.DatabaseBootstrap/Dockerfile" \
  "mortgages-web:PD.Mortgages.Web/Dockerfile" \
  "mortgages-staff-web:PD.Mortgages.Staff.Web/Dockerfile" \
  "mortgages-api:PD.Mortgages.Api/Dockerfile" \
  "notifications-worker:PD.Notifications.Worker/Dockerfile" \
  "products-api:PD.Products.Api/Dockerfile" \
  "eligibility-api:PD.Eligibility.Api/Dockerfile"; do
  image="${image_and_dockerfile%%:*}"
  dockerfile="${image_and_dockerfile#*:}"
  az acr build --registry "$registry" --image "$image:latest" --file "$dockerfile" .
done

echo "Deploying the private database bootstrap job..."
deploy mortgage-bootstrap false true false
job_name="$(az deployment group show --name mortgage-bootstrap --resource-group "$resource_group" \
  --query properties.outputs.databaseBootstrapJobName.value --output tsv)"
execution="$(az containerapp job start --name "$job_name" --resource-group "$resource_group" --query name --output tsv)"
if [[ -z "$execution" || "$execution" == "None" ]]; then
  echo "The bootstrap job did not return an execution name." >&2
  exit 1
fi

bootstrap_succeeded=false
for ((attempt=1; attempt<=120; attempt++)); do
  status="$(az containerapp job execution show --name "$job_name" --resource-group "$resource_group" \
    --job-execution-name "$execution" --query properties.status --output tsv)"
  case "$status" in
    Succeeded) bootstrap_succeeded=true; break ;;
    Failed|Stopped|Degraded)
      echo "Database bootstrap execution $execution ended with status $status. Inspect the job logs in Azure." >&2
      exit 1 ;;
  esac
  sleep 15
done
if [[ "$bootstrap_succeeded" != true ]]; then
  echo "Database bootstrap did not finish in time. Inspect execution $execution in Azure before retrying." >&2
  exit 1
fi

echo "Deploying applications..."
deploy mortgage-apps true true false
customer_url="$(az deployment group show --name mortgage-apps --resource-group "$resource_group" \
  --query properties.outputs.mortgagesWebUrl.value --output tsv)"
staff_url="$(az deployment group show --name mortgage-apps --resource-group "$resource_group" \
  --query properties.outputs.staffWebUrl.value --output tsv)"

if [[ "$staff_url" != https://* ]]; then
  echo "Deployment did not return an HTTPS staff URL." >&2
  exit 1
fi
# Fetch again immediately before updating so redirects added during deployment are retained.
staff_app="$(az ad app show --id "$staff_client_id" --output json)"
if ! jq -e --arg id "$staff_object_id" '.id == $id' <<< "$staff_app" >/dev/null; then
  echo "The staff registration changed during deployment; refusing to update callbacks." >&2
  exit 1
fi
staff_update="$(jq -ce --arg signin "$staff_url/signin-oidc" --arg signout "$staff_url/signout-callback-oidc" \
  '(.web.redirectUris // []) as $uris | if ($uris | type == "array" and all(.[]; type == "string")) and
     ((.web.redirectUriSettings // []) | type == "array" and all(.[]; .index == null)) then
    {web: (((.web // {}) | del(.redirectUriSettings)) + {redirectUris:($uris + [$signin, $signout] | unique)})}
   else error("Invalid staff redirect URI list") end' <<< "$staff_app")"
if ! jq -e --arg signin "$staff_url/signin-oidc" --arg signout "$staff_url/signout-callback-oidc" \
  '(.web.redirectUris // []) as $uris | ($uris | index($signin)) != null and ($uris | index($signout)) != null' <<< "$staff_app" >/dev/null; then
  az rest --method patch --url "https://graph.microsoft.com/v1.0/applications/$staff_object_id" \
    --body "$staff_update" --output none
  callbacks_verified=false
  for ((attempt=1; attempt<=6; attempt++)); do
    staff_after="$(az ad app show --id "$staff_client_id" --output json)"
    if jq -e --argjson before "$staff_app" --arg signin "$staff_url/signin-oidc" --arg signout "$staff_url/signout-callback-oidc" '
      .web as $web | (.web.redirectUris // []) as $uris |
      ($uris | index($signin)) != null and ($uris | index($signout)) != null and
      all(($before.web.redirectUris // [])[]; . as $old | ($uris | index($old)) != null) and
      all((($before.web // {}) | to_entries[] | select(.key != "redirectUris" and .key != "redirectUriSettings")); . as $entry | $web[$entry.key] == $entry.value)
    ' <<< "$staff_after" >/dev/null; then
      callbacks_verified=true
      break
    fi
    sleep 5
  done
  if [[ "$callbacks_verified" != true ]]; then
    echo "Staff callbacks and web settings could not be verified; inspect the registration and restore any missing settings before retrying." >&2
    exit 1
  fi
fi

for url in "$customer_url" "$staff_url"; do
  if [[ "$url" != https://* ]]; then
    echo "Deployment did not return an HTTPS application URL." >&2
    exit 1
  fi
  ready=false
  for ((attempt=1; attempt<=60; attempt++)); do
    if curl --silent --show-error --fail --max-time 10 "$url/health/ready" >/dev/null; then
      ready=true
      break
    fi
    sleep 10
  done
  if [[ "$ready" != true ]]; then
    echo "Readiness check failed for $url. Inspect the Container App logs." >&2
    exit 1
  fi
done

if [[ "$configure_github" == true ]]; then
  gh variable set AZURE_SUBSCRIPTION_ID --repo "$github_repo" --body "$subscription_id"
  gh variable set AZURE_RESOURCE_GROUP --repo "$github_repo" --body "$resource_group"
  gh variable set AZURE_CONTAINER_REGISTRY --repo "$github_repo" --body "$registry"
  echo "GitHub repository variables updated. Allow time for Azure role assignments to propagate."
fi
echo "Customer app: $customer_url"
echo "Staff app: $staff_url"
if [[ "$automated" != true && "$configure_github" != true ]]; then
  echo "Configure GitHub deployment permissions and variables manually before running the workflow."
fi
echo "Readiness checks do not test sign-in, document uploads or a complete submission."
echo "Resources remain billable. Review Azure Cost Management and delete the resource group when finished."
if [[ "$automated" == true && -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  printf '## Prototype deployed\n\n- Customer: %s\n- Staff: %s\n\nResources remain billable until removed.\n' \
    "$customer_url" "$staff_url" >> "$GITHUB_STEP_SUMMARY"
fi
