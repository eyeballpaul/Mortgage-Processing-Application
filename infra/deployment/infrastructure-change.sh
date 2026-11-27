#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${GITHUB_REPOSITORY:?Missing GITHUB_REPOSITORY}"
: "${GITHUB_SHA:?Missing GITHUB_SHA}"
: "${INFRA_ACTION:?Missing INFRA_ACTION}"
: "${INFRA_RESULT:?Missing INFRA_RESULT}"

if [[ ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ||
      ! "$GITHUB_REPOSITORY" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  echo "Invalid repository or commit." >&2
  exit 1
fi
if [[ "$(az account show --query id --output tsv)" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "Selected Azure subscription does not match the configured subscription." >&2
  exit 1
fi
if [[ "$(az group exists --name "$AZURE_RESOURCE_GROUP" --output tsv)" != true ]]; then
  echo "The prototype resource group does not exist." >&2
  exit 1
fi
work="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-infra.XXXXXXXX")"
trap 'rm -f -- "$work/template.json" "$work/changes.json" "$work/infra-preview.json"; rmdir "$work"' EXIT
az bicep build --file infra/changes.bicep --stdout > "$work/template.json"
if ! jq -e '
  (.resources | type == "array") and
  ([.. | objects | select((.type? // "" | ascii_downcase) == "microsoft.resources/deployments")] | length == 0)
' "$work/template.json" >/dev/null; then
  echo "The change template must not contain nested deployments or modules." >&2
  exit 1
fi

az deployment group what-if --name mortgage-change-preview \
  --resource-group "$AZURE_RESOURCE_GROUP" --mode Incremental \
  --template-file "$work/template.json" --parameters "releaseCommit=$GITHUB_SHA" \
  --no-pretty-print --output json |
  jq -ceS '
    if (.changes | type) != "array" then error("Missing what-if changes")
    elif ((.diagnostics // []) | length) != 0 then error("What-if reported diagnostics; inspect them in Azure before applying")
    elif (.nextLink // "") != "" or .error != null then error("What-if returned an incomplete or failed result")
    elif .status != null and .status != "Succeeded" then error("What-if did not succeed")
    else .changes | sort_by(.resourceId) end
  ' > "$work/changes.json"
if ! jq -e '
  type == "array" and all(.[];
    (.resourceId | type == "string" and length > 0) and
    (.changeType == "Create" or .changeType == "Ignore"))
' "$work/changes.json" >/dev/null; then
  echo "Preview includes existing or indeterminate resources; only new resources are allowed. Review the template and Azure what-if." >&2
  exit 1
fi
echo "Preview resource IDs and change types (review the committed template for full properties):"
jq '[.[] | select(.changeType != "Ignore") | {resourceId, changeType}]' "$work/changes.json"
changes_hash="$(sha256sum "$work/changes.json" | cut -d ' ' -f1)"
template_hash="$(sha256sum "$work/template.json" | cut -d ' ' -f1)"

case "$INFRA_ACTION" in
  preview)
    jq -n --arg commit "$GITHUB_SHA" --arg subscription "$AZURE_SUBSCRIPTION_ID" \
      --arg group "$AZURE_RESOURCE_GROUP" --arg changes "$changes_hash" \
      --arg template "$template_hash" \
      '{commit: $commit, subscription: $subscription, resourceGroup: $group, changesHash: $changes, templateHash: $template}' \
      > "$INFRA_RESULT"
    ;;
  apply)
    if [[ ! "${PREVIEW_RUN_ID:-}" =~ ^[0-9]+$ ]]; then
      echo "Provide a successful preview workflow run ID." >&2
      exit 1
    fi
    run="$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$PREVIEW_RUN_ID")"
    if ! jq -e --arg commit "$GITHUB_SHA" '
      .path == ".github/workflows/prototype-infrastructure.yml" and
      .event == "workflow_dispatch" and .head_branch == "main" and
      .head_sha == $commit and .status == "completed" and .conclusion == "success" and
      (.run_attempt | type == "number" and . >= 1)
    ' <<< "$run" >/dev/null; then
      echo "The preview run must have succeeded on the current main commit." >&2
      exit 1
    fi
    attempt="$(jq -er '.run_attempt' <<< "$run")"
    gh run download "$PREVIEW_RUN_ID" --repo "$GITHUB_REPOSITORY" \
      --name "infra-preview-$attempt" --dir "$work"
    if ! jq -e --arg commit "$GITHUB_SHA" --arg subscription "$AZURE_SUBSCRIPTION_ID" \
      --arg group "$AZURE_RESOURCE_GROUP" --arg changes "$changes_hash" --arg template "$template_hash" '
      .commit == $commit and .subscription == $subscription and .resourceGroup == $group and
      .changesHash == $changes and .templateHash == $template
    ' "$work/infra-preview.json" >/dev/null; then
      echo "The template or Azure preview changed since review; run and review a new preview." >&2
      exit 1
    fi
    az deployment group create --name mortgage-reviewed-change \
      --resource-group "$AZURE_RESOURCE_GROUP" --mode Incremental \
      --template-file "$work/template.json" --parameters "releaseCommit=$GITHUB_SHA" --output none
    echo "Incremental infrastructure deployment completed."
    ;;
  *) echo "Unknown infrastructure action." >&2; exit 1 ;;
esac
