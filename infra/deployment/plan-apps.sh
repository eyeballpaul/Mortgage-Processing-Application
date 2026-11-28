#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
: "${GITHUB_SHA:?Missing GITHUB_SHA}"
: "${APP_PLAN:?Missing APP_PLAN}"

if [[ ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ||
      "$(az account show --query id --output tsv)" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "Invalid target commit or Azure subscription." >&2
  exit 1
fi
if [[ "$(az group exists --name "$AZURE_RESOURCE_GROUP" --output tsv)" != true ]]; then
  echo "Prototype resource group is absent; the deployment plan is unavailable."
  exit 0
fi
registries="$(az resource list --resource-group "$AZURE_RESOURCE_GROUP" \
  --resource-type Microsoft.ContainerRegistry/registries --name "$AZURE_CONTAINER_REGISTRY" --output json)"
if ! jq -e 'type == "array" and length <= 1' <<< "$registries" >/dev/null; then
  echo "Invalid registry listing." >&2
  exit 1
fi
if jq -e 'length == 0' <<< "$registries" >/dev/null; then
  echo "Prototype registry is absent; the deployment plan is unavailable."
  exit 0
fi
registry="$(az acr show --name "$AZURE_CONTAINER_REGISTRY" --resource-group "$AZURE_RESOURCE_GROUP" \
  --query loginServer --output tsv)"
if [[ ! "$registry" =~ ^[A-Za-z0-9][A-Za-z0-9.-]+$ ]]; then
  echo "Invalid registry login server." >&2
  exit 1
fi
apps="$(az containerapp list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
if ! jq -e 'type == "array" and all(.[]; .name | type == "string" and length > 0)' <<< "$apps" >/dev/null; then
  echo "Invalid Container Apps listing." >&2
  exit 1
fi

targets='[]'
for service in "${app_services[@]}"; do
  project="$(service_project "$service")"
  repository="$(service_image "$service")"
  app_name="$(service_app_name "$service")"
  count="$(jq -r --arg name "$app_name" '[.[] | select(.name == $name)] | length' <<< "$apps")"
  if [[ "$count" == 0 ]]; then
    targets="$(jq -cn --argjson targets "$targets" --arg service "$service" \
      '$targets + [{service:$service,status:"missing",reason:"App is absent; create or repair its infrastructure before applying the plan"}]')"
    continue
  elif [[ "$count" != 1 ]]; then
    echo "Duplicate Container App entries for $service." >&2
    exit 1
  fi
  app="$(az containerapp show --name "$app_name" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$app")"
  image="$(jq -er '.properties.template.containers | select(length == 1) | .[0].image | select(type == "string" and length > 0)' <<< "$app")"
  if ! jq -e '.properties.configuration.activeRevisionsMode == "Single"' <<< "$app" >/dev/null; then
    echo "$service must use single-revision mode before it can be planned." >&2
    exit 1
  fi
  revisions="$(az containerapp revision list --name "$app_name" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  active="$(jq -ce '
    if type != "array" then error("Invalid revision list") else
      [ .[] | select(.properties.active == true) |
        {name: .name, image: .properties.template.containers[0].image} ] | sort_by(.name)
    end' <<< "$revisions")"
  if ! jq -e 'all(.[]; (.name | type == "string" and length > 0) and (.image | type == "string" and length > 0))' <<< "$active" >/dev/null; then
    echo "$service returned an invalid active revision." >&2
    exit 1
  fi
  baseline="$(jq -r '.tags["pd-deployed-commit"] // ""' <<< "$app")"
  digest="$(jq -r '.tags["pd-deployed-digest"] // ""' <<< "$app")"
  status=deploy
  reason="Unverified deployment baseline; rebuild and review"
  if [[ "$baseline" =~ ^[0-9a-f]{40}$ && "$digest" =~ ^sha256:[0-9a-f]{64}$ &&
        "$image" == "$registry/$repository@$digest" ]] &&
     jq -e --arg image "$image" 'length == 1 and .[0].image == $image' <<< "$active" >/dev/null; then
    if ! git merge-base --is-ancestor "$baseline" "$GITHUB_SHA"; then
      echo "$service has a deployed commit that is not an ancestor of the target; inspect it before planning." >&2
      exit 1
    fi
    changed="$(python3 infra/deployment/affected.py \
      --project "$project/$project.csproj" --base "$baseline" --head "$GITHUB_SHA")"
    case "$changed" in
      true) reason="Project inputs changed since the verified deployment" ;;
      false) status=unchanged; reason="No project input changes since the verified deployment" ;;
      *) echo "Invalid change detection result for $service." >&2; exit 1 ;;
    esac
  fi
  targets="$(jq -cn --argjson targets "$targets" --arg service "$service" --arg id "$id" \
    --arg image "$image" --argjson active "$active" --arg baseline "$baseline" --arg digest "$digest" \
    --arg status "$status" --arg reason "$reason" \
    '$targets + [{service:$service,id:$id,sourceImage:$image,activeRevisions:$active,sourceCommit:$baseline,sourceDigest:$digest,status:$status,reason:$reason}]')"
done

jobs="$(az containerapp job list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
job_count="$(jq -er 'if type == "array" then [.[] | select(.name == "mortgage-demo-env-bootstrap")] | length
  else error("Invalid job listing") end' <<< "$jobs")"
if [[ "$job_count" -gt 1 ]]; then
  echo "Duplicate bootstrap jobs were returned." >&2
  exit 1
fi
bootstrap_id=''
bootstrap_image=''
bootstrap_fingerprint=''
bootstrap_commit=''
bootstrap_digest=''
if [[ "$job_count" == 1 ]]; then
  job="$(az containerapp job show --name mortgage-demo-env-bootstrap --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  bootstrap_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$job")"
  bootstrap_image="$(jq -er '.properties.template.containers | select(length == 1) | .[0].image | select(type == "string" and length > 0)' <<< "$job")"
  bootstrap_fingerprint="$(bootstrap_fingerprint <<< "$job")"
  bootstrap_commit="$(jq -r '.tags["pd-bootstrap-commit"] // ""' <<< "$job")"
  bootstrap_digest="$(jq -r '.tags["pd-bootstrap-digest"] // ""' <<< "$job")"
fi
bootstrap_build=true
bootstrap_reason="Bootstrap is missing or its baseline is unverified; build and run it after reviewing migration compatibility"
if [[ "$bootstrap_commit" =~ ^[0-9a-f]{40}$ ]] &&
   ! git merge-base --is-ancestor "$bootstrap_commit" "$GITHUB_SHA"; then
  echo "The bootstrap commit is not an ancestor of the target; inspect it before planning." >&2
  exit 1
fi
if [[ "$bootstrap_commit" =~ ^[0-9a-f]{40}$ && "$bootstrap_digest" =~ ^sha256:[0-9a-f]{64}$ &&
      "$bootstrap_image" == "$registry/database-bootstrap@$bootstrap_digest" ]]; then
  bootstrap_project="$(service_project database-bootstrap)"
  bootstrap_build="$(python3 infra/deployment/affected.py \
    --project "$bootstrap_project/$bootstrap_project.csproj" --base "$bootstrap_commit" --head "$GITHUB_SHA")"
  case "$bootstrap_build" in
    true) bootstrap_reason="Bootstrap inputs changed; build and run after reviewing migration compatibility" ;;
    false) bootstrap_reason="No bootstrap input changes detected" ;;
    *) echo "Invalid bootstrap change detection result." >&2; exit 1 ;;
  esac
fi

jq -n --arg commit "$GITHUB_SHA" --arg subscription "$AZURE_SUBSCRIPTION_ID" \
  --arg group "$AZURE_RESOURCE_GROUP" --arg registry "$AZURE_CONTAINER_REGISTRY" \
  --argjson targets "$targets" --argjson bootstrapBuild "$bootstrap_build" \
  --arg bootstrapReason "$bootstrap_reason" --arg bootstrapId "$bootstrap_id" \
  --arg bootstrapImage "$bootstrap_image" --arg bootstrapCommit "$bootstrap_commit" \
  --arg bootstrapDigest "$bootstrap_digest" --arg bootstrapFingerprint "$bootstrap_fingerprint" \
  '{commit:$commit,subscription:$subscription,resourceGroup:$group,registry:$registry,targets:$targets,bootstrapBuild:$bootstrapBuild,bootstrapReason:$bootstrapReason,bootstrap:{id:$bootstrapId,sourceImage:$bootstrapImage,sourceCommit:$bootstrapCommit,sourceDigest:$bootstrapDigest,fingerprint:$bootstrapFingerprint}}' > "$APP_PLAN"
