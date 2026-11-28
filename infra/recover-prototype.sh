#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

for command in az gh jq; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Missing required command: $command" >&2
    exit 1
  fi
done

if ! az account show --output none; then
  echo "Sign in as an Azure administrator and select the target subscription first." >&2
  exit 1
fi
if ! gh auth status --hostname github.com >/dev/null; then
  echo "Sign in to GitHub CLI with permission to manage this repository's variables and secrets." >&2
  exit 1
fi

subscription="$(az account show --query id --output tsv)"
tenant="$(az account show --query tenantId --output tsv)"
echo "Selected subscription: $subscription"
echo "Selected tenant: $tenant"
read -r -p "GitHub repository (owner/name): " github_repo
if [[ ! "$github_repo" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  echo "Enter a GitHub repository as owner/name." >&2
  exit 1
fi
github_repo="$(gh repo view "$github_repo" --json nameWithOwner --jq .nameWithOwner)"
github_client_id="$(gh variable get AZURE_CLIENT_ID --repo "$github_repo")"
github_tenant_id="$(gh variable get AZURE_TENANT_ID --repo "$github_repo")"
api_client_id="$(gh variable get MORTGAGE_API_CLIENT_ID --repo "$github_repo")"
staff_client_id="$(gh variable get STAFF_CLIENT_ID --repo "$github_repo")"
if [[ -z "$github_client_id" || "$github_tenant_id" != "$tenant" ]]; then
  echo "The repository's deployment app must already exist and its tenant must match the selected Azure tenant." >&2
  exit 1
fi
if [[ -z "$api_client_id" || -z "$staff_client_id" ]]; then
  echo "The repository must already contain MORTGAGE_API_CLIENT_ID and STAFF_CLIENT_ID variables." >&2
  exit 1
fi

read -r -p "Name of the deleted prototype resource group: " resource_group
if [[ ! "$resource_group" =~ ^[A-Za-z0-9._\(\)-]{1,90}$ ]]; then
  echo "The resource group name contains unsupported characters." >&2
  exit 1
fi
if [[ "$(az group exists --name "$resource_group" --output tsv)" != false ]]; then
  echo "The resource group already exists. Use the normal create/reconcile workflow instead of full-group recovery." >&2
  exit 1
fi

echo "This will create billable resources and restore permissions in:"
echo "  Subscription: $subscription"
echo "  Resource group: $resource_group"
echo "  GitHub repository: $github_repo"
echo "You will need the existing staff secret and service API keys from your password manager."
read -r -p "Proceed to the interactive Azure deployment? Type yes: " confirm
if [[ "$confirm" != yes ]]; then
  echo "Recovery cancelled."
  exit 1
fi

export AZURE_RESOURCE_GROUP="$resource_group"
export GITHUB_REPOSITORY="$github_repo"
export MORTGAGE_API_CLIENT_ID="$api_client_id"
export STAFF_CLIENT_ID="$staff_client_id"
bash infra/quickstart-deploy.sh

export AZURE_CLIENT_ID="$github_client_id"
bash infra/setup-deployment-permissions.sh
bash infra/setup-notification-permissions.sh

read -r -s -p "DEPLOYMENT_FINGERPRINT_KEY (64 hex characters, from your password manager): " fingerprint_key
echo
if [[ ! "$fingerprint_key" =~ ^[0-9a-fA-F]{64}$ ]]; then
  unset fingerprint_key
  echo "DEPLOYMENT_FINGERPRINT_KEY must be 32 random bytes encoded as 64 hexadecimal characters." >&2
  exit 1
fi
export DEPLOYMENT_FINGERPRINT_KEY="$fingerprint_key"
unset fingerprint_key
bash infra/deployment/initialize-stacks.sh
unset DEPLOYMENT_FINGERPRINT_KEY

echo "Resource-group recovery completed. Verify the customer and staff URLs printed by the quickstart."
