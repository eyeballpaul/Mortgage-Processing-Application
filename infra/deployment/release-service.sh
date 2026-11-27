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
RELEASE_COMMIT="$commit" RELEASE_DIGEST="$digest" bash infra/deployment/deploy-image.sh
