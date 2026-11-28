#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

for command in az gh jq openssl curl uuidgen; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Missing required command: $command" >&2
    exit 1
  fi
done

if ! az account show --output none; then
  echo "Sign in as an Azure and Entra administrator and select the target subscription first." >&2
  exit 1
fi
if ! gh auth status --hostname github.com >/dev/null; then
  echo "Sign in to GitHub CLI with permission to manage this repository's variables and secrets." >&2
  exit 1
fi

subscription="$(az account show --query id --output tsv)"
tenant="$(az account show --query tenantId --output tsv)"
signed_in_user="$(az ad signed-in-user show --query userPrincipalName --output tsv)"
echo "Selected subscription: $subscription"
echo "Selected tenant: $tenant"
echo "Signed-in Entra user: $signed_in_user"
echo "This creates Azure resources and Entra applications. Azure budgets do not cap spending."

read -r -p "GitHub repository (owner/name): " github_repo
if [[ ! "$github_repo" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  echo "Enter a GitHub repository as owner/name." >&2
  exit 1
fi
github_repo="$(gh repo view "$github_repo" --json nameWithOwner --jq .nameWithOwner)"
read -r -p "Staff user's Entra sign-in (UPN): " staff_upn
if [[ -z "$staff_upn" || "$staff_upn" == *$'\n'* ]]; then
  echo "A staff user's Entra UPN is required." >&2
  exit 1
fi
read -r -p "New resource group name: " resource_group
if [[ ! "$resource_group" =~ ^[A-Za-z0-9._\(\)-]{1,90}$ ]]; then
  echo "The resource group name contains unsupported characters." >&2
  exit 1
fi
if [[ "$(az group exists --name "$resource_group" --output tsv)" != false ]]; then
  echo "That resource group already exists. Inspect it and use the recovery path instead." >&2
  exit 1
fi

for display_name in "Mortgage API" "Staff Web" "GitHub deployment"; do
  existing="$(az ad app list --filter "displayName eq '$display_name'" --all --output json)"
  if [[ "$(jq --arg name "$display_name" '[.[] | select(.displayName == $name)] | length' <<< "$existing")" != 0 ]]; then
    echo "An app registration named '$display_name' already exists. This fresh bootstrap will not adopt or overwrite it; use recovery or inspect the existing setup." >&2
    exit 1
  fi
done

suffix="$(openssl rand -hex 4)"
read -r -p "Azure region [uksouth]: " location
location="${location:-uksouth}"
read -r -p "Globally unique registry name [mortgagedemo$suffix]: " registry
registry="${registry:-mortgagedemo$suffix}"
read -r -p "SQL server name [mortgage-demo-sql-$suffix]: " sql_server
sql_server="${sql_server:-mortgage-demo-sql-$suffix}"
read -r -p "Storage account name [mortgagest$suffix]: " storage
storage="${storage:-mortgagest$suffix}"
if [[ ! "$registry" =~ ^[a-z0-9]{5,50}$ || ! "$sql_server" =~ ^[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$ ||
  ! "$storage" =~ ^[a-z0-9]{3,24}$ ]]; then
  echo "The registry, SQL server, or storage account name is invalid." >&2
  exit 1
fi

repo_json="$(gh api "repos/$github_repo" --jq '{owner:.owner.login,owner_id:.owner.id,name:.name,id:.id}')"
owner="$(jq -er '.owner' <<< "$repo_json")"
owner_id="$(jq -er '.owner_id | tostring' <<< "$repo_json")"
repo_name="$(jq -er '.name' <<< "$repo_json")"
repo_id="$(jq -er '.id | tostring' <<< "$repo_json")"
oidc_subject="repo:$owner@$owner_id/$repo_name@$repo_id:ref:refs/heads/main"

api_scope_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"
customer_role_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"
api_staff_role_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"
web_staff_role_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"

create_app() {
  local display_name="$1" app_json
  app_json="$(az ad app create --display-name "$display_name" --sign-in-audience AzureADMyOrg --output json)"
  jq -er '{objectId:.id,clientId:.appId} | select(.objectId != null and .clientId != null)' <<< "$app_json"
}

wait_for_service_principal() {
  local client_id="$1" output=""
  for ((attempt=1; attempt<=12; attempt++)); do
    if output="$(az ad sp show --id "$client_id" --output json 2>/dev/null)"; then
      jq -er '.id | select(type == "string" and length > 0)' <<< "$output"
      return
    fi
    sleep 5
  done
  echo "Timed out waiting for service principal $client_id." >&2
  return 1
}

create_service_principal() {
  local client_id="$1"
  az ad sp create --id "$client_id" --output none
  wait_for_service_principal "$client_id"
}

new_api="$(create_app "Mortgage API")"
api_object_id="$(jq -er '.objectId' <<< "$new_api")"
api_client_id="$(jq -er '.clientId' <<< "$new_api")"
api_config="$(jq -n \
  --arg identifier "api://$api_client_id" \
  --arg scope "$api_scope_id" \
  --arg customer "$customer_role_id" \
  --arg staff "$api_staff_role_id" '{
    identifierUris: [$identifier],
    api: {
      requestedAccessTokenVersion: 2,
      oauth2PermissionScopes: [{
        id: $scope,
        adminConsentDisplayName: "Access Mortgage API as staff",
        adminConsentDescription: "Allow the staff application to call the Mortgage API as the signed-in staff user.",
        isEnabled: true,
        type: "Admin",
        userConsentDisplayName: null,
        userConsentDescription: null,
        value: "Mortgage.Staff"
      }]
    },
    appRoles: [
      {allowedMemberTypes:["Application"],description:"Access customer mortgage sessions.",displayName:"Mortgage Customer",id:$customer,isEnabled:true,value:"MortgageCustomer"},
      {allowedMemberTypes:["User","Group"],description:"Access staff mortgage operations.",displayName:"Mortgage Staff",id:$staff,isEnabled:true,value:"MortgageStaff"}
    ]
  }')"
az rest --method patch --url "https://graph.microsoft.com/v1.0/applications/$api_object_id" --body "$api_config" --output none
api_sp_id="$(create_service_principal "$api_client_id")"

new_staff="$(create_app "Staff Web")"
staff_object_id="$(jq -er '.objectId' <<< "$new_staff")"
staff_client_id="$(jq -er '.clientId' <<< "$new_staff")"
staff_config="$(jq -n \
  --arg scope "$api_scope_id" \
  --arg api "$api_client_id" \
  --arg role "$web_staff_role_id" '{
    web: {
      redirectUris:["https://localhost:7241/signin-oidc"],
      implicitGrantSettings:{enableIdTokenIssuance:true}
    },
    requiredResourceAccess:[{
      resourceAppId:$api,
      resourceAccess:[{id:$scope,type:"Scope"}]
    }],
    appRoles:[
      {allowedMemberTypes:["User","Group"],description:"Access the staff mortgage application.",displayName:"Mortgage Staff",id:$role,isEnabled:true,value:"MortgageStaff"}
    ]
  }')"
az rest --method patch --url "https://graph.microsoft.com/v1.0/applications/$staff_object_id" --body "$staff_config" --output none
staff_sp_id="$(create_service_principal "$staff_client_id")"

new_deployment="$(create_app "GitHub deployment")"
deployment_object_id="$(jq -er '.objectId' <<< "$new_deployment")"
deployment_client_id="$(jq -er '.clientId' <<< "$new_deployment")"
deployment_sp_id="$(create_service_principal "$deployment_client_id")"

credential_file="$(mktemp "${TMPDIR:-/tmp}/mortgage-federated-credential.XXXXXXXX")"
chmod 600 "$credential_file"
trap 'rm -f -- "$credential_file"' EXIT
jq -n --arg subject "$oidc_subject" '{
  name:"github-main-immutable",
  issuer:"https://token.actions.githubusercontent.com",
  subject:$subject,
  audiences:["api://AzureADTokenExchange"]
}' > "$credential_file"
az ad app federated-credential create --id "$deployment_object_id" --parameters "$credential_file" --output none

graph_api_id=00000003-0000-0000-c000-000000000000
graph_read_all=9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30
graph_owned_by=18a4783c-866b-4cc7-a460-3d5e5662c884
deployment_graph_config="$(jq -n --arg graph "$graph_api_id" --arg read "$graph_read_all" --arg owned "$graph_owned_by" '{
  requiredResourceAccess:[{
    resourceAppId:$graph,
    resourceAccess:[{id:$read,type:"Role"},{id:$owned,type:"Role"}]
  }]
}')"
az rest --method patch --url "https://graph.microsoft.com/v1.0/applications/$deployment_object_id" \
  --body "$deployment_graph_config" --output none
echo "Granting Microsoft Graph application permissions now. This requires an authorized Entra administrator."
az ad app permission admin-consent --id "$deployment_client_id" --output none

echo "Granting the staff app's Mortgage.Staff delegated permission. This requires authorized admin consent."
az ad app permission admin-consent --id "$staff_client_id" --output none
az ad app owner add --id "$staff_client_id" --owner-object-id "$deployment_sp_id" --output none

staff_user_id="$(az ad user show --id "$staff_upn" --query id --output tsv)"
if [[ ! "$staff_user_id" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  echo "The staff user could not be resolved to a directory object ID." >&2
  exit 1
fi
assign_staff_role() {
  local resource_sp="$1" role_id="$2" list body
  list="$(az rest --method get --url "https://graph.microsoft.com/v1.0/servicePrincipals/$resource_sp/appRoleAssignedTo" --output json)"
  if ! jq -e --arg user "$staff_user_id" --arg resource "$resource_sp" --arg role "$role_id" \
    'any(.value[]; .principalId == $user and .resourceId == $resource and .appRoleId == $role)' <<< "$list" >/dev/null; then
    body="$(jq -n --arg user "$staff_user_id" --arg resource "$resource_sp" --arg role "$role_id" \
      '{principalId:$user,resourceId:$resource,appRoleId:$role}')"
    az rest --method post --url "https://graph.microsoft.com/v1.0/servicePrincipals/$resource_sp/appRoleAssignedTo" \
      --body "$body" --output none
  fi
}
assign_staff_role "$api_sp_id" "$api_staff_role_id"
assign_staff_role "$staff_sp_id" "$web_staff_role_id"

staff_credentials="$(az ad app credential reset --id "$staff_client_id" --append --display-name "Mortgage prototype Staff Web" --years 1 --output json)"
staff_secret="$(jq -er '.password | select(type == "string" and length > 0)' <<< "$staff_credentials")"
products_key="$(openssl rand -hex 32)"
eligibility_key="$(openssl rand -hex 32)"
deployment_fingerprint_key="$(openssl rand -hex 32)"

echo
echo "Save these generated values in your password manager now; they cannot be retrieved from GitHub later."
printf 'STAFF_CLIENT_SECRET=%s\n' "$staff_secret"
printf 'PRODUCTS_API_KEY=%s\n' "$products_key"
printf 'ELIGIBILITY_API_KEY=%s\n' "$eligibility_key"
printf 'DEPLOYMENT_FINGERPRINT_KEY=%s\n' "$deployment_fingerprint_key"
read -r -p "Have you saved all four values securely? Type yes to continue: " secrets_saved
if [[ "$secrets_saved" != yes ]]; then
  unset staff_secret products_key eligibility_key deployment_fingerprint_key
  echo "Setup stopped. Entra applications have been created; save the values before continuing." >&2
  exit 1
fi

set_repo_secret() {
  local name="$1" value="$2"
  printf '%s' "$value" | gh secret set "$name" --repo "$github_repo"
}
set_repo_secret STAFF_CLIENT_SECRET "$staff_secret"
set_repo_secret PRODUCTS_API_KEY "$products_key"
set_repo_secret ELIGIBILITY_API_KEY "$eligibility_key"
set_repo_secret DEPLOYMENT_FINGERPRINT_KEY "$deployment_fingerprint_key"
gh variable set AZURE_CLIENT_ID --repo "$github_repo" --body "$deployment_client_id"
gh variable set AZURE_TENANT_ID --repo "$github_repo" --body "$tenant"
gh variable set AZURE_SUBSCRIPTION_ID --repo "$github_repo" --body "$subscription"
gh variable set AZURE_RESOURCE_GROUP --repo "$github_repo" --body "$resource_group"
gh variable set AZURE_CONTAINER_REGISTRY --repo "$github_repo" --body "$registry"
gh variable set AZURE_SQL_SERVER --repo "$github_repo" --body "$sql_server"
gh variable set AZURE_STORAGE_ACCOUNT --repo "$github_repo" --body "$storage"
gh variable set MORTGAGE_API_CLIENT_ID --repo "$github_repo" --body "$api_client_id"
gh variable set STAFF_CLIENT_ID --repo "$github_repo" --body "$staff_client_id"

echo
echo "Created app registrations:"
echo "  Mortgage API client ID: $api_client_id"
echo "  Staff Web client ID: $staff_client_id"
echo "  GitHub deployment client ID: $deployment_client_id"
echo "The GitHub OIDC subject is restricted to $github_repo on main."
echo "A failure from this point can leave Azure/Entra resources in place. Inspect before retrying."
read -r -p "Continue to deploy the prototype? Type yes: " deploy_confirm
if [[ "$deploy_confirm" != yes ]]; then
  unset staff_secret products_key eligibility_key deployment_fingerprint_key
  echo "Applications and GitHub settings are configured. Run infra/recover-prototype.sh after reviewing them." >&2
  exit 1
fi

export AZURE_RESOURCE_GROUP="$resource_group"
export GITHUB_REPOSITORY="$github_repo"
export MORTGAGE_API_CLIENT_ID="$api_client_id"
export STAFF_CLIENT_ID="$staff_client_id"
quickstart_input="$(printf '%s\n' "$location" "$registry" "$sql_server" "$storage" "$staff_secret" \
  "$products_key" "$eligibility_key" y "$github_repo" yes)"
printf '%s\n' "$quickstart_input" | bash infra/quickstart-deploy.sh
unset quickstart_input

export AZURE_CLIENT_ID="$deployment_client_id"
bash infra/setup-deployment-permissions.sh
bash infra/setup-notification-permissions.sh
export DEPLOYMENT_FINGERPRINT_KEY="$deployment_fingerprint_key"
bash infra/deployment/initialize-stacks.sh
unset DEPLOYMENT_FINGERPRINT_KEY
unset staff_secret products_key eligibility_key deployment_fingerprint_key

echo "Fresh prototype setup completed. Test both applications and retain the generated secrets securely."
