#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
: "${AZURE_SQL_SERVER:?Missing AZURE_SQL_SERVER}"
: "${AZURE_STORAGE_ACCOUNT:?Missing AZURE_STORAGE_ACCOUNT}"
: "${AZURE_CLIENT_ID:?Missing AZURE_CLIENT_ID}"
: "${MORTGAGE_API_CLIENT_ID:?Missing MORTGAGE_API_CLIENT_ID}"
: "${STAFF_CLIENT_ID:?Missing STAFF_CLIENT_ID}"
: "${STAFF_CLIENT_SECRET:?Missing STAFF_CLIENT_SECRET}"
: "${PRODUCTS_API_KEY:?Missing PRODUCTS_API_KEY}"
: "${ELIGIBILITY_API_KEY:?Missing ELIGIBILITY_API_KEY}"
: "${GITHUB_REPOSITORY:?Missing GITHUB_REPOSITORY}"
: "${GITHUB_SHA:?Missing GITHUB_SHA}"
: "${DEPLOYMENT_FINGERPRINT_KEY:?Missing DEPLOYMENT_FINGERPRINT_KEY}"

if [[ ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ||
      ! "$GITHUB_REPOSITORY" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ||
      ! "$DEPLOYMENT_FINGERPRINT_KEY" =~ ^[0-9a-fA-F]{64}$ ]]; then
  echo "Invalid repository, commit or deployment fingerprint key." >&2
  exit 1
fi
if [[ "$(gh api "repos/$GITHUB_REPOSITORY/git/ref/heads/main" --jq .object.sha)" != "$GITHUB_SHA" ]]; then
  echo "Main changed after this workflow started; run the deployment again." >&2
  exit 1
fi
runs="$(gh api "repos/$GITHUB_REPOSITORY/actions/workflows/ci.yml/runs?branch=main&event=push&status=success&head_sha=$GITHUB_SHA&per_page=10")"
if ! jq -e --arg sha "$GITHUB_SHA" '
  .workflow_runs | type == "array" and any(.[];
    .path == ".github/workflows/ci.yml" and .head_branch == "main" and .event == "push" and
    .head_sha == $sha and .status == "completed" and .conclusion == "success")
' <<< "$runs" >/dev/null; then
  echo "The current main commit needs a successful push build and test run before deployment." >&2
  exit 1
fi

umask 077
work="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-reconcile.XXXXXXXX")"
cleanup() {
  local status=$?
  rm -f -- "$work/parameters.json" "$work/plan.json" "$work/manifest.json" || status=1
  rmdir "$work" || status=1
  if [[ "$status" != 0 ]]; then
    echo "Reconciliation stopped; earlier changes or partial migrations may remain. Inspect the error and retry." >&2
  fi
  exit "$status"
}
trap cleanup EXIT
export STACK_PARAMETERS="$work/parameters.json"
export APP_PLAN="$work/plan.json" RELEASE_MANIFEST="$work/manifest.json"

stacks="$(az stack group list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
if ! jq -e '
  type == "array" and ([.[] | select(.name == "pd-foundation")] | length == 1) and
  all(.[]; (.name | type == "string") and
    (if (.name | startswith("pd-app-")) then
      .tags["pd-owner"] == "mortgage-prototype" and
      .tags["pd-target"] == (.name | ltrimstr("pd-app-")) and
      (.tags["pd-target"] | test("^[a-z][a-z0-9-]{0,40}$"))
     else true end))
' <<< "$stacks" >/dev/null; then
  echo "Prototype ownership is not initialized. Delete and recreate the prototype first." >&2
  exit 1
fi
while IFS= read -r target; do
  STACK_SERVICE="$target" STACK_ACTION=check bash infra/deployment/app-stack.sh
done < <(jq -r '.[] | select(.tags["pd-owner"] == "mortgage-prototype" and
  (.name | startswith("pd-app-"))) | .tags["pd-target"]' <<< "$stacks")

STACK_TARGET=foundation bash infra/deployment/stack-parameters.sh
bash infra/deployment/foundation-stack.sh
bash infra/deployment/plan-apps.sh
if [[ ! -f "$APP_PLAN" ]]; then
  echo "The prototype could not be planned; recreate it before deploying." >&2
  exit 1
fi
bash infra/deployment/build-images.sh
if ! jq -e --arg commit "$GITHUB_SHA" --arg registry "$AZURE_CONTAINER_REGISTRY" \
  '.commit == $commit and .registry == $registry and (.images | type == "object")' "$RELEASE_MANIFEST" >/dev/null; then
  echo "The image build manifest is invalid." >&2
  exit 1
fi
registry="$(az acr show --name "$AZURE_CONTAINER_REGISTRY" --resource-group "$AZURE_RESOURCE_GROUP" \
  --query loginServer --output tsv)"

image_for_build() {
  local service="$1" repository digest
  repository="$(service_image "$service")" || return 1
  if ! digest="$(jq -er --arg service "$service" --arg repository "$repository" '
    .images[$service] | select(.repository == $repository) |
    .digest | select(type == "string" and test("^sha256:[0-9a-f]{64}$"))' "$RELEASE_MANIFEST")"; then
    echo "The image build manifest has no valid digest for $service." >&2
    return 1
  fi
  printf '%s/%s@%s\n' "$registry" "$repository" "$digest"
}

bootstrap_image="$(jq -r '.bootstrap.sourceImage' "$APP_PLAN")"
bootstrap_missing=false
if [[ -z "$bootstrap_image" ]]; then
  bootstrap_missing=true
  bootstrap_image="$(image_for_build database-bootstrap)"
fi
STACK_TARGET=database-bootstrap STACK_IMAGE="$bootstrap_image" bash infra/deployment/stack-parameters.sh
STACK_SERVICE=database-bootstrap STACK_ACTION=apply STACK_IMAGE="$bootstrap_image" \
  ALLOW_CREATE="$bootstrap_missing" bash infra/deployment/app-stack.sh

foundation="$(az stack group show --resource-group "$AZURE_RESOURCE_GROUP" --name pd-foundation --output json)"
foundation_sha="$(jq -er '.tags["pd-template-sha"] | select(type == "string" and test("^[0-9a-f]{64}$"))' <<< "$foundation")"
migrated_sha="$(jq -r '.tags["pd-migrated-sha"] // ""' <<< "$foundation")"
if [[ "$(jq -r '.bootstrapBuild' "$APP_PLAN")" == true || "$migrated_sha" != "$foundation_sha" ]]; then
  if [[ "$(jq -r '.bootstrapBuild' "$APP_PLAN")" == true ]]; then
    bootstrap_image="$(image_for_build database-bootstrap)"
    bootstrap_digest="${bootstrap_image##*@}"
  else
    bootstrap_digest="$(jq -er '.bootstrap.sourceDigest | select(test("^sha256:[0-9a-f]{64}$"))' "$APP_PLAN")"
  fi
  RELEASE_SERVICE=database-bootstrap RELEASE_COMMIT="$GITHUB_SHA" RELEASE_DIGEST="$bootstrap_digest" \
    bash infra/deployment/deploy-image.sh
  STACK_TARGET=foundation bash infra/deployment/stack-parameters.sh
  STACK_MIGRATED_SHA="$foundation_sha" bash infra/deployment/foundation-stack.sh
fi

for service in "${app_services[@]}"; do
  status="$(jq -er --arg service "$service" '.targets[] | select(.service == $service) | .status' "$APP_PLAN")"
  case "$status" in
    missing) app_image="$(image_for_build "$service")"; allow_create=true ;;
    deploy|unchanged)
      app_image="$(jq -er --arg service "$service" '.targets[] | select(.service == $service) | .sourceImage' "$APP_PLAN")"
      allow_create=false ;;
    *) echo "Invalid plan status for $service." >&2; exit 1 ;;
  esac
  STACK_TARGET="$service" STACK_IMAGE="$app_image" bash infra/deployment/stack-parameters.sh
  STACK_SERVICE="$service" STACK_ACTION=apply STACK_IMAGE="$app_image" ALLOW_CREATE="$allow_create" \
    bash infra/deployment/app-stack.sh
  if [[ "$status" != unchanged ]]; then
    built_image="$(image_for_build "$service")"
    RELEASE_SERVICE="$service" RELEASE_COMMIT="$GITHUB_SHA" RELEASE_DIGEST="${built_image##*@}" \
      bash infra/deployment/deploy-image.sh
  fi
  if [[ "$service" == staff-web ]]; then
    bash infra/deployment/sync-staff-callbacks.sh
  fi
done

while IFS= read -r target; do
  declared=false
  for service in "${app_services[@]}" database-bootstrap; do
    if [[ "$service" == "$target" ]]; then declared=true; break; fi
  done
  if [[ "$declared" != true ]]; then
    if [[ "$target" == staff-web ]]; then
      STAFF_CALLBACK_ACTION=remove bash infra/deployment/sync-staff-callbacks.sh
    fi
    STACK_SERVICE="$target" STACK_ACTION=delete bash infra/deployment/app-stack.sh
  fi
done < <(jq -r '.[] | select(.tags["pd-owner"] == "mortgage-prototype" and
  (.name | startswith("pd-app-"))) | .tags["pd-target"] // empty' <<< "$stacks")

echo "Prototype reconciliation completed. Verify application behaviour and review Azure costs."
