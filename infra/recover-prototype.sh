#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

# Check command-line clients before querying either Azure or GitHub.
for command in az gh jq openssl; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Missing required command: $command" >&2
    exit 1
  fi
done

# Recovery is run by an administrator, not by the GitHub deployment identity.
if ! az account show --output none; then
  echo "Sign in as an Azure administrator and select the target subscription first." >&2
  exit 1
fi
if ! gh auth status --hostname github.com >/dev/null; then
  echo "Sign in to GitHub CLI with permission to manage this repository's variables and secrets." >&2
  exit 1
fi

# Capture the selected context so the user can verify the exact target before changes.
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

# Reuse the app registrations retained outside the deleted resource group.
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

# Reuse resource names retained in repository variables to avoid accidental name changes.
registry="$(gh variable get AZURE_CONTAINER_REGISTRY --repo "$github_repo")"
sql_server="$(gh variable get AZURE_SQL_SERVER --repo "$github_repo")"
storage="$(gh variable get AZURE_STORAGE_ACCOUNT --repo "$github_repo")"
if [[ -z "$registry" || -z "$sql_server" || -z "$storage" ]]; then
  echo "The repository must retain AZURE_CONTAINER_REGISTRY, AZURE_SQL_SERVER and AZURE_STORAGE_ACCOUNT variables." >&2
  exit 1
fi

# Reuse the saved group location when retrying after a partially completed deployment.
read -r -p "Name of the prototype resource group (deleted or partial): " resource_group
if [[ ! "$resource_group" =~ ^[A-Za-z0-9._\(\)-]{1,90}$ ]]; then
  echo "The resource group name contains unsupported characters." >&2
  exit 1
fi
group_exists="$(az group exists --name "$resource_group" --output tsv)"
case "$group_exists" in
  false)
    read -r -p "Region for the recreated resource group [uksouth]: " location
    location="${location:-uksouth}"
    ;;
  true)
    location="$(az group show --name "$resource_group" --query location --output tsv)"
    echo "The resource group already exists in $location; this supports retrying a partially completed setup."
    echo "Continue only if this is the dedicated prototype group and its resources may be reconciled."
    read -r -p "Confirm this is the prototype group? Type yes: " group_confirm
    if [[ "$group_confirm" != yes ]]; then
      echo "Recovery cancelled."
      exit 1
    fi
    ;;
  *)
    echo "Could not determine whether the resource group exists." >&2
    exit 1
    ;;
esac

# Hidden prompts are used because GitHub cannot reveal existing secret values.
read -r -s -p "Existing Staff Web client secret value: " staff_secret
echo
read -r -s -p "Existing Products API key: " products_key
echo
read -r -s -p "Existing Eligibility API key: " eligibility_key
echo
read -r -s -p "DEPLOYMENT_FINGERPRINT_KEY (64 hex characters, from your password manager): " fingerprint_key
echo
if [[ -z "$staff_secret" ||
  ! "$products_key" =~ ^[A-Za-z0-9_-]{32,128}$ ||
  ! "$eligibility_key" =~ ^[A-Za-z0-9_-]{32,128}$ ||
  "$products_key" == "$eligibility_key" ||
  ! "$fingerprint_key" =~ ^[0-9a-fA-F]{64}$ ]]; then
  unset staff_secret products_key eligibility_key fingerprint_key
  echo "A required secret is empty, malformed, or duplicated. No Azure changes were made." >&2
  exit 1
fi

# Confirm scope and cost impact before writing temporary secret material or deploying resources.
echo "This will create or reconcile billable resources and restore permissions in:"
echo "  Subscription: $subscription"
echo "  Resource group: $resource_group ($location)"
echo "  GitHub repository: $github_repo"
read -r -p "Proceed with resource creation? Type yes: " confirm
if [[ "$confirm" != yes ]]; then
  unset staff_secret products_key eligibility_key fingerprint_key
  echo "Recovery cancelled."
  exit 1
fi

# Keep secret material in a private temporary file, not in command arguments or simulated prompt input.
setup_config="$(mktemp "${TMPDIR:-/tmp}/mortgage-recovery-config.XXXXXXXX")"
chmod 600 "$setup_config"
cleanup() {
  rm -f -- "$setup_config"
  unset staff_secret products_key eligibility_key fingerprint_key
}
trap cleanup EXIT
printf '%s\n' "$subscription" "$tenant" "$resource_group" "$location" "$registry" "$sql_server" \
  "$storage" "$api_client_id" "$staff_client_id" "$staff_secret" "$products_key" \
  "$eligibility_key" "$github_repo" | bash infra/deployment/write-setup-config.sh "$setup_config"

# Run the same explicitly configured Azure deployment as the first-time setup path.
export AZURE_RESOURCE_GROUP="$resource_group"
export AZURE_CLIENT_ID="$github_client_id"
PROTOTYPE_SETUP_CONFIG="$setup_config" bash infra/quickstart-deploy.sh

# Keep the stable key available only while initializing stack fingerprints.
export DEPLOYMENT_FINGERPRINT_KEY="$fingerprint_key"
unset fingerprint_key
bash infra/deployment/finalize-prototype-setup.sh
unset DEPLOYMENT_FINGERPRINT_KEY

echo "Resource-group recovery completed. Verify the customer and staff URLs printed by the quickstart."
