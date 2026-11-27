#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
: "${RELEASE_SERVICE:?Missing RELEASE_SERVICE}"
: "${RELEASE_COMMIT:?Missing RELEASE_COMMIT}"
: "${RELEASE_DIGEST:?Missing RELEASE_DIGEST}"

if [[ ! "$RELEASE_COMMIT" =~ ^[0-9a-f]{40}$ || ! "$RELEASE_DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]]; then
  echo "Invalid release commit or image digest." >&2
  exit 1
fi
if [[ "$(az account show --query id --output tsv)" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "Selected Azure subscription does not match the configured subscription." >&2
  exit 1
fi
image="$(service_image "$RELEASE_SERVICE")"
az acr repository show --name "$AZURE_CONTAINER_REGISTRY" --image "$image@$RELEASE_DIGEST" --output none
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
    --image "$registry/$image@$RELEASE_DIGEST" --output none
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
  az tag update --resource-id "$job_id" --operation merge \
    --tags "pd-bootstrap-commit=$RELEASE_COMMIT" "pd-bootstrap-digest=$RELEASE_DIGEST" --output none
  echo "Database bootstrap completed. Check migration compatibility before updating applications."
  exit 0
fi

app="$(az containerapp show --name "$RELEASE_SERVICE" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
app_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$app")"
if [[ "$RELEASE_SERVICE" == mortgage-api ]]; then
  verify_mortgage_identity "$app"
fi
current="$(jq -r '.tags["pd-deployed-commit"] // ""' <<< "$app")"
if [[ "$current" =~ ^[0-9a-f]{40}$ && "$current" != "$RELEASE_COMMIT" ]] &&
   ! git merge-base --is-ancestor "$current" "$RELEASE_COMMIT" &&
   [[ "${ALLOW_ROLLBACK:-false}" != true ]]; then
  echo "The selected commit is not a descendant of the deployed commit. Explicitly approve a rollback after reviewing database compatibility." >&2
  exit 1
fi
az containerapp update --name "$RELEASE_SERVICE" --resource-group "$AZURE_RESOURCE_GROUP" \
  --image "$registry/$image@$RELEASE_DIGEST" --output none
revision_ready=false
for ((attempt=1; attempt<=60; attempt++)); do
  revisions="$(az containerapp revision list --name "$RELEASE_SERVICE" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  if jq -e --arg image "$registry/$image@$RELEASE_DIGEST" '
    type == "array" and ([.[] | select(.properties.active == true)] |
      length == 1 and .[0].properties.template.containers[0].image == $image)
  ' <<< "$revisions" >/dev/null; then
    revision_ready=true
    break
  fi
  sleep 10
done
if [[ "$revision_ready" != true ]]; then
  echo "The selected image did not become the sole active revision; inspect Azure before retrying." >&2
  exit 1
fi
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
az tag update --resource-id "$app_id" --operation merge \
  --tags "pd-deployed-commit=$RELEASE_COMMIT" "pd-deployed-digest=$RELEASE_DIGEST" --output none
echo "Applied $RELEASE_SERVICE image from commit $RELEASE_COMMIT. Verify its revision and application behaviour in Azure."
