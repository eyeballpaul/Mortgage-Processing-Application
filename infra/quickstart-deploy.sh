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

principal_id="$(az ad signed-in-user show --query id --output tsv)"
if [[ -z "$principal_id" || "$principal_id" == "None" ]]; then
  echo "An interactive Entra user login is required." >&2
  exit 1
fi
subscription_name="$(az account show --query name --output tsv)"
subscription_id="$(az account show --query id --output tsv)"
echo "Selected subscription: $subscription_name ($subscription_id)"
echo "This deployment creates billable resources. An Azure budget does not cap spending."
echo "Azure SQL free-offer databases become unavailable for the rest of the month when their free compute is exhausted."
echo "This script does not pause resources. Delete the resource group when finished if you do not need its data."

read -r -p "Existing resource group name: " resource_group
if [[ -z "$resource_group" ]] || [[ "$(az group exists --name "$resource_group" --output tsv)" != "true" ]]; then
  echo "The named resource group must already exist in the selected subscription." >&2
  exit 1
fi
location="$(az group show --name "$resource_group" --query location --output tsv)"
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

if [[ -z "$api_client_id" || -z "$staff_client_id" || -z "$staff_secret" ]]; then
  echo "Both client IDs and the staff secret are required." >&2
  exit 1
fi
if [[ ! "$products_key" =~ ^[A-Za-z0-9_-]{32,128}$ || ! "$eligibility_key" =~ ^[A-Za-z0-9_-]{32,128}$ || "$products_key" == "$eligibility_key" ]]; then
  echo "The API keys must be different and each contain 32-128 permitted characters." >&2
  exit 1
fi

echo "Resource group: $resource_group ($location)"
echo "Registry: $registry; SQL server: $sql_server; storage: $storage"
echo "The signed-in user needs resource-group role-assignment permission and Entra permission to assign app roles."
read -r -p "Create or update these Azure resources? Type yes to continue: " confirm
if [[ "$confirm" != "yes" ]]; then
  echo "Deployment cancelled."
  exit 1
fi

api_sp="$(az ad sp show --id "$api_client_id" --output json)"
api_principal_id="$(jq -er '.id' <<< "$api_sp")"
customer_role_id="$(jq -er '[.appRoles[] | select(.value == "MortgageCustomer" and .isEnabled and (.allowedMemberTypes | index("Application")))] | if length == 1 then .[0].id else error("MortgageCustomer application role missing or ambiguous") end' <<< "$api_sp")"

parameter_file="$(mktemp "${TMPDIR:-/tmp}/mortgage-parameters.XXXXXXXX")"
chmod 600 "$parameter_file"
trap 'rm -f -- "$parameter_file"' EXIT

export location registry sql_server storage api_client_id staff_client_id staff_secret principal_id products_key eligibility_key
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
    pipelinePrincipalType: {value: "User"},
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

echo "Building images in Azure Container Registry..."
for image_and_dockerfile in \
  "database-bootstrap:PD.Mortgages.DatabaseBootstrap/Dockerfile" \
  "mortgages-web:PD.Mortgages.Web/Dockerfile" \
  "mortgages-staff-web:PD.Mortgages.Staff.Web/Dockerfile" \
  "mortgages-api:PD.Mortgages.Api/Dockerfile" \
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

echo "Customer app: $customer_url"
echo "Staff app: $staff_url"
echo "Configure the staff registration's redirect and sign-out callback URLs before testing sign-in."
echo "Readiness checks do not test sign-in, document uploads or a complete submission."
echo "Resources remain billable. Review Azure Cost Management and delete the resource group when finished."
