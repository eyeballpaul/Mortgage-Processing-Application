#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
: "${GITHUB_REPOSITORY:?Missing GITHUB_REPOSITORY}"
: "${GITHUB_SHA:?Missing GITHUB_SHA}"
: "${RELEASE_RUN_ID:?Missing RELEASE_RUN_ID}"

if [[ "${REVIEW_CONFIRMED:-false}" != true ]]; then
  echo "Review the plan, migrations, infrastructure and compatibility before applying." >&2
  exit 1
fi
if [[ ! "$RELEASE_RUN_ID" =~ ^[0-9]+$ || ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ||
      ! "$GITHUB_REPOSITORY" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  echo "Invalid repository, commit or workflow run ID." >&2
  exit 1
fi
if [[ "$(az account show --query id --output tsv)" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "Selected Azure subscription does not match the configured subscription." >&2
  exit 1
fi
main_sha="$(gh api "repos/$GITHUB_REPOSITORY/git/ref/heads/main" --jq .object.sha)"
if [[ "$main_sha" != "$GITHUB_SHA" ]]; then
  echo "Main changed after this workflow started; create and review a new plan." >&2
  exit 1
fi
run="$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$RELEASE_RUN_ID")"
if ! jq -e --arg commit "$GITHUB_SHA" '
  .path == ".github/workflows/ci.yml" and .head_branch == "main" and
  (.event == "push" or .event == "workflow_dispatch") and
  .status == "completed" and .conclusion == "success" and .head_sha == $commit and
  (.run_attempt | type == "number" and . >= 1)
' <<< "$run" >/dev/null; then
  echo "Select a successful build-and-test run for the current main commit." >&2
  exit 1
fi
attempt="$(jq -er '.run_attempt' <<< "$run")"
work="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-plan.XXXXXXXX")"
trap 'rm -f -- "$work/app-plan.json" "$work/release-manifest.json"; rmdir "$work"' EXIT
gh run download "$RELEASE_RUN_ID" --repo "$GITHUB_REPOSITORY" \
  --name "app-plan-$attempt" --dir "$work"
gh run download "$RELEASE_RUN_ID" --repo "$GITHUB_REPOSITORY" \
  --name "release-manifest-$attempt" --dir "$work"
plan="$work/app-plan.json"
manifest="$work/release-manifest.json"
expected_services="$(printf '%s\n' "${app_services[@]}" | jq -R -s 'split("\n")[:-1]')"
if jq -e 'any(.targets[]; .status == "missing")' "$plan" >/dev/null; then
  echo "The plan contains a missing app; create or repair it, then generate and review a new plan." >&2
  exit 1
fi
if ! jq -e --arg commit "$GITHUB_SHA" --arg subscription "$AZURE_SUBSCRIPTION_ID" \
  --arg group "$AZURE_RESOURCE_GROUP" --arg registry "$AZURE_CONTAINER_REGISTRY" \
  --argjson services "$expected_services" --slurpfile manifest "$manifest" '
  .commit == $commit and .subscription == $subscription and .resourceGroup == $group and
  .registry == $registry and $manifest[0].commit == $commit and $manifest[0].registry == $registry and
  (.bootstrapBuild | type == "boolean") and
  (.bootstrap.id | type == "string" and length > 0) and
  (.bootstrap.sourceImage | type == "string" and length > 0) and
  (.bootstrap.sourceCommit | type == "string") and
  (.bootstrap.sourceDigest | type == "string") and
  (.bootstrap.fingerprint | type == "string" and test("^[0-9a-f]{64}$")) and
  (if .bootstrapBuild then
    $manifest[0].images["database-bootstrap"].repository == "database-bootstrap" and
    ($manifest[0].images["database-bootstrap"].digest | type == "string" and test("^sha256:[0-9a-f]{64}$"))
   else true end) and
  ([.targets[].service] == $services) and
  all(.targets[];
    (.id | type == "string" and length > 0) and
    (.sourceImage | type == "string" and length > 0) and
    (.activeRevisions | type == "array") and
    (.sourceCommit | type == "string") and (.sourceDigest | type == "string") and
    (.status == "deploy" or .status == "unchanged") and
    (if .status == "deploy" then .service as $s |
      ($manifest[0].images[$s].digest | type == "string" and test("^sha256:[0-9a-f]{64}$"))
    else true end))
' "$plan" >/dev/null; then
  echo "The plan or image manifest is invalid or does not match the target environment." >&2
  exit 1
fi

job="$(az containerapp job show --name mortgage-demo-env-bootstrap --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
job_fingerprint="$(bootstrap_fingerprint <<< "$job")"
if ! jq -e --slurpfile plan "$plan" '
  .id == $plan[0].bootstrap.id and
  (.properties.template.containers | length == 1 and .[0].image == $plan[0].bootstrap.sourceImage) and
  (.tags["pd-bootstrap-commit"] // "") == $plan[0].bootstrap.sourceCommit and
  (.tags["pd-bootstrap-digest"] // "") == $plan[0].bootstrap.sourceDigest
' <<< "$job" >/dev/null ||
   [[ "$job_fingerprint" != "$(jq -r '.bootstrap.fingerprint' "$plan")" ]]; then
  echo "The bootstrap job changed since the plan was generated; create and review a new plan." >&2
  exit 1
fi
if [[ "$(jq -r '.bootstrapBuild' "$plan")" == true ]]; then
  bootstrap_digest="$(jq -er '.images["database-bootstrap"].digest' "$manifest")"
  az acr repository show --name "$AZURE_CONTAINER_REGISTRY" \
    --image "database-bootstrap@$bootstrap_digest" --output none
fi
for service in "${app_services[@]}"; do
  expected="$(jq -ce --arg service "$service" '.targets[] | select(.service == $service)' "$plan")"
  app_name="$(service_app_name "$service")"
  app="$(az containerapp show --name "$app_name" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  if ! jq -e --argjson expected "$expected" '
    .id == $expected.id and
    .properties.configuration.activeRevisionsMode == "Single" and
    (.properties.template.containers | length == 1 and .[0].image == $expected.sourceImage) and
    (.tags["pd-deployed-commit"] // "") == $expected.sourceCommit and
    (.tags["pd-deployed-digest"] // "") == $expected.sourceDigest
  ' <<< "$app" >/dev/null; then
    echo "$service changed since the plan was generated; create and review a new plan." >&2
    exit 1
  fi
  revisions="$(az containerapp revision list --name "$app_name" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  if ! jq -e --argjson expected "$expected" '
    type == "array" and
    ([.[] | select(.properties.active == true) |
      {name: .name, image: .properties.template.containers[0].image}] | sort_by(.name)) == $expected.activeRevisions
  ' <<< "$revisions" >/dev/null; then
    echo "$service active revisions changed since the plan; create and review a new plan." >&2
    exit 1
  fi
  if [[ "$(jq -r '.status' <<< "$expected")" == deploy ]]; then
    baseline="$(jq -r '.sourceCommit' <<< "$expected")"
    if [[ "$baseline" =~ ^[0-9a-f]{40}$ && "$baseline" != "$GITHUB_SHA" ]] &&
       ! git merge-base --is-ancestor "$baseline" "$GITHUB_SHA"; then
      echo "$service would require a rollback; use the individual release workflow after review." >&2
      exit 1
    fi
    if [[ "$service" == mortgage-api ]]; then
      verify_mortgage_identity "$app"
    fi
    image="$(service_image "$service")"
    digest="$(jq -er --arg service "$service" '.images[$service].digest' "$manifest")"
    if [[ "$(jq -r --arg service "$service" '.images[$service].repository' "$manifest")" != "$image" ]]; then
      echo "Image repository does not match $service." >&2
      exit 1
    fi
    az acr repository show --name "$AZURE_CONTAINER_REGISTRY" --image "$image@$digest" --output none
  fi
done

if [[ "$(jq -r '.bootstrapBuild' "$plan")" == true ]]; then
  echo "Running the reviewed database bootstrap before application updates."
  RELEASE_SERVICE=database-bootstrap bash infra/deployment/release-service.sh
fi
echo "Applying the reviewed app plan in dependency order. A failure may leave earlier apps updated."
for service in "${app_services[@]}"; do
  if [[ "$(jq -r --arg service "$service" '.targets[] | select(.service == $service) | .status' "$plan")" == deploy ]]; then
    echo "Deploying $service."
    RELEASE_SERVICE="$service" ALLOW_ROLLBACK=false bash infra/deployment/release-service.sh
  fi
done
echo "Application plan completed. Verify revisions and end-to-end behaviour."
