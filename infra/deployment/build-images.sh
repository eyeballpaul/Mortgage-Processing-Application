#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
: "${GITHUB_SHA:?Missing GITHUB_SHA}"
: "${RELEASE_MANIFEST:?Missing RELEASE_MANIFEST}"

if [[ ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Expected a full Git commit SHA." >&2
  exit 1
fi

if [[ "$(az group exists --name "$AZURE_RESOURCE_GROUP" --output tsv)" != true ]]; then
  echo "Prototype resource group is absent; image builds are skipped."
  exit 0
fi
registries="$(az resource list --resource-group "$AZURE_RESOURCE_GROUP" \
  --resource-type Microsoft.ContainerRegistry/registries --name "$AZURE_CONTAINER_REGISTRY" --output json)"
if ! jq -e 'type == "array" and length <= 1' <<< "$registries" >/dev/null; then
  echo "Invalid registry listing." >&2
  exit 1
fi
if jq -e 'length == 0' <<< "$registries" >/dev/null; then
  echo "Prototype registry is absent; image builds are skipped."
  exit 0
fi

images='{}'
for service in products-api eligibility-api mortgage-api mortgages-web staff-web notifications-worker database-bootstrap; do
  project="$(service_project "$service")"
  image="$(service_image "$service")"
  build=true
  if [[ "${BUILD_ALL:-false}" != true && "${BASE_SHA:-}" =~ ^[0-9a-f]{40}$ ]] &&
     git merge-base --is-ancestor "$BASE_SHA" "$GITHUB_SHA"; then
    build="$(python3 infra/deployment/affected.py \
      --project "$project/$project.csproj" --base "$BASE_SHA" --head "$GITHUB_SHA")"
  fi
  if [[ "$build" == false ]]; then
    echo "No build required for $service."
    continue
  fi
  if [[ "$build" != true ]]; then
    echo "Invalid change detection result for $service." >&2
    exit 1
  fi
  echo "Building $service for commit $GITHUB_SHA."
  az acr build --registry "$AZURE_CONTAINER_REGISTRY" --image "$image:$GITHUB_SHA" \
    --file "$project/Dockerfile" . --output none
  digest="$(az acr repository show --name "$AZURE_CONTAINER_REGISTRY" \
    --image "$image:$GITHUB_SHA" --query digest --output tsv)"
  if [[ ! "$digest" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    echo "Registry did not return a valid image digest for $service." >&2
    exit 1
  fi
  images="$(jq -cn --argjson images "$images" --arg service "$service" \
    --arg image "$image" --arg digest "$digest" \
    '$images + {($service): {repository: $image, digest: $digest}}')"
done
jq -n --arg commit "$GITHUB_SHA" --arg registry "$AZURE_CONTAINER_REGISTRY" \
  --argjson images "$images" '{commit: $commit, registry: $registry, images: $images}' > "$RELEASE_MANIFEST"
echo "Built image digests recorded for this successful test run."
