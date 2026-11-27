#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
: "${GITHUB_REPOSITORY:?Missing GITHUB_REPOSITORY}"
: "${RELEASE_RUN_ID:?Missing RELEASE_RUN_ID}"
: "${RELEASE_SERVICE:?Missing RELEASE_SERVICE}"

if [[ ! "$RELEASE_RUN_ID" =~ ^[0-9]+$ ||
      ! "$GITHUB_REPOSITORY" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  echo "Invalid repository or workflow run ID." >&2
  exit 1
fi
image="$(service_image "$RELEASE_SERVICE")"
if [[ "$(az account show --query id --output tsv)" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "Selected Azure subscription does not match the configured subscription." >&2
  exit 1
fi
run="$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$RELEASE_RUN_ID")"
if ! jq -e '
  .path == ".github/workflows/ci.yml" and .head_branch == "main" and
  (.event == "push" or .event == "workflow_dispatch") and
  .status == "completed" and .conclusion == "success" and
  (.head_sha | type == "string" and test("^[0-9a-f]{40}$")) and
  (.run_attempt | type == "number" and . >= 1)
' <<< "$run" >/dev/null; then
  echo "The selected run is not a successful main build-and-test run." >&2
  exit 1
fi
commit="$(jq -er '.head_sha' <<< "$run")"
attempt="$(jq -er '.run_attempt' <<< "$run")"
release_dir="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-release.XXXXXXXX")"
trap 'rm -f -- "$release_dir/release-manifest.json"; rmdir "$release_dir"' EXIT
gh run download "$RELEASE_RUN_ID" --repo "$GITHUB_REPOSITORY" \
  --name "release-manifest-$attempt" --dir "$release_dir"
manifest="$release_dir/release-manifest.json"
if ! jq -e --arg commit "$commit" --arg registry "$AZURE_CONTAINER_REGISTRY" \
  --arg service "$RELEASE_SERVICE" --arg image "$image" '
  .commit == $commit and .registry == $registry and
  .images[$service].repository == $image and
  (.images[$service].digest | type == "string" and test("^sha256:[0-9a-f]{64}$"))
' "$manifest" >/dev/null; then
  echo "The selected run did not build this target for the configured registry." >&2
  exit 1
fi
digest="$(jq -er --arg service "$RELEASE_SERVICE" '.images[$service].digest' "$manifest")"
az acr repository show --name "$AZURE_CONTAINER_REGISTRY" --image "$image@$digest" --output none
registry="$(az acr show --name "$AZURE_CONTAINER_REGISTRY" --resource-group "$AZURE_RESOURCE_GROUP" \
  --query loginServer --output tsv)"
if [[ ! "$registry" =~ ^[A-Za-z0-9][A-Za-z0-9.-]+$ ]]; then
  echo "The configured registry did not return a valid login server." >&2
  exit 1
fi

if [[ "$RELEASE_SERVICE" == database-bootstrap ]]; then
  job=mortgage-demo-env-bootstrap
  job_id="$(az containerapp job show --name "$job" --resource-group "$AZURE_RESOURCE_GROUP" --query id --output tsv)"
  if [[ -z "$job_id" || "$job_id" == None ]]; then
    echo "The existing bootstrap job was not found." >&2
    exit 1
  fi
  az containerapp job update --name "$job" --resource-group "$AZURE_RESOURCE_GROUP" \
    --image "$registry/$image@$digest" --output none
  execution="$(az containerapp job start --name "$job" --resource-group "$AZURE_RESOURCE_GROUP" \
    --query name --output tsv)"
  if [[ -z "$execution" || "$execution" == None ]]; then
    echo "The bootstrap job did not return an execution name." >&2
    exit 1
  fi
  succeeded=false
  for ((attempt=1; attempt<=120; attempt++)); do
    status="$(az containerapp job execution show --name "$job" --resource-group "$AZURE_RESOURCE_GROUP" \
      --job-execution-name "$execution" --query properties.status --output tsv)"
    case "$status" in
      Succeeded) succeeded=true; break ;;
      Failed|Stopped|Degraded)
        echo "Bootstrap execution $execution ended with status $status." >&2
        exit 1 ;;
    esac
    sleep 15
  done
  if [[ "$succeeded" != true ]]; then
    echo "Bootstrap execution $execution timed out; inspect Azure before retrying." >&2
    exit 1
  fi
  az tag update --resource-id "$job_id" --operation merge --tags "pd-bootstrap-commit=$commit" --output none
  echo "Database bootstrap completed. Check migration compatibility before updating applications."
  exit 0
fi

app="$(az containerapp show --name "$RELEASE_SERVICE" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
app_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$app")"
if [[ "$RELEASE_SERVICE" == mortgage-api ]]; then
  identity="$(az identity show --name mortgage-demo-env-mortgages --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  identity_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$identity")"
  identity_client="$(jq -er '.clientId | select(type == "string" and length > 0)' <<< "$identity")"
  if ! jq -e --arg id "$identity_id" --arg client "$identity_client" '
    any((.identity.userAssignedIdentities // {}) | keys[]; ascii_downcase == ($id | ascii_downcase)) and
    any(.properties.template.containers[0].env[]?; .name == "AZURE_CLIENT_ID" and .value == $client) and
    any(.properties.template.containers[0].env[]?; .name == "Notifications__WorkerModel" and .value == "separate")
  ' <<< "$app" >/dev/null; then
    echo "Mortgage API identity or separate-worker configuration is not ready; follow the legacy cutover guide first." >&2
    exit 1
  fi
fi
current="$(jq -r '.tags["pd-deployed-commit"] // ""' <<< "$app")"
if [[ "$current" =~ ^[0-9a-f]{40}$ && "$current" != "$commit" ]] &&
   ! git merge-base --is-ancestor "$current" "$commit" &&
   [[ "${ALLOW_ROLLBACK:-false}" != true ]]; then
  echo "The selected commit is not a descendant of the deployed commit. Explicitly approve a rollback after reviewing database compatibility." >&2
  exit 1
fi
az containerapp update --name "$RELEASE_SERVICE" --resource-group "$AZURE_RESOURCE_GROUP" \
  --image "$registry/$image@$digest" --output none
if [[ "$RELEASE_SERVICE" == mortgages-web || "$RELEASE_SERVICE" == staff-web ]]; then
  hostname="$(az containerapp show --name "$RELEASE_SERVICE" --resource-group "$AZURE_RESOURCE_GROUP" \
    --query properties.configuration.ingress.fqdn --output tsv)"
  if [[ ! "$hostname" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]; then
    echo "The updated app did not return a valid ingress hostname." >&2
    exit 1
  fi
  ready=false
  for ((attempt=1; attempt<=60; attempt++)); do
    if curl --silent --show-error --fail --max-time 10 "https://$hostname/health/ready" >/dev/null; then
      ready=true
      break
    fi
    sleep 10
  done
  if [[ "$ready" != true ]]; then
    echo "The updated app did not pass its public readiness check." >&2
    exit 1
  fi
fi
az tag update --resource-id "$app_id" --operation merge --tags "pd-deployed-commit=$commit" --output none
echo "Applied $RELEASE_SERVICE image from commit $commit. Verify its revision and application behaviour in Azure."
