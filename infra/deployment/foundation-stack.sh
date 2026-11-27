#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${STACK_PARAMETERS:?Missing STACK_PARAMETERS}"

if [[ "$(az account show --query id --output tsv)" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "Selected Azure subscription does not match the configured subscription." >&2
  exit 1
fi
group="$(az group show --name "$AZURE_RESOURCE_GROUP" --query id --output tsv)"
expected_group="/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$AZURE_RESOURCE_GROUP"
if ! jq -en --arg group "$group" --arg expected "$expected_group" \
  '($group | ascii_downcase) == ($expected | ascii_downcase)' >/dev/null; then
  echo "Resource group does not match the configured subscription." >&2
  exit 1
fi
if [[ ! -f "$STACK_PARAMETERS" ]] || ! jq -e '
  (.parameters.createSqlServer.value | type == "boolean") and .parameters.deployAlerts.value == true
' "$STACK_PARAMETERS" >/dev/null; then
  echo "Foundation parameters must specify SQL creation and include alert ownership." >&2
  exit 1
fi
stack=pd-foundation
stacks="$(az stack group list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
if ! jq -e --arg name "$stack" '
  type == "array" and ([.[] | select(.name == $name)] | length <= 1)
' <<< "$stacks" >/dev/null; then
  echo "Invalid or ambiguous deployment stack listing." >&2
  exit 1
fi
exists="$(jq -r --arg name "$stack" 'any(.[]; .name == $name)' <<< "$stacks")"
verify_stack() {
  jq -e --arg name "$stack" --arg group "$group" '
    (.id | type == "string" and ascii_downcase == (($group + "/providers/Microsoft.Resources/deploymentStacks/" + $name) | ascii_downcase)) and
    .tags["pd-owner"] == "mortgage-prototype" and .tags["pd-target"] == "foundation" and
    (.resources | type == "array" and length > 0 and all(.[];
      .status == "managed" and
      (.id | type == "string" and
        (ascii_downcase | startswith(($group | ascii_downcase) + "/providers/")) and
        (ascii_downcase | contains("/providers/microsoft.app/containerapps/") | not) and
        (ascii_downcase | contains("/providers/microsoft.app/jobs/") | not))))
  ' >/dev/null
}
if [[ "$exists" == true ]]; then
  current="$(az stack group show --resource-group "$AZURE_RESOURCE_GROUP" --name "$stack" --output json)"
  if ! verify_stack <<< "$current"; then
    echo "The foundation stack has unexpected ownership or resources; no changes were made." >&2
    exit 1
  fi
elif [[ "${ALLOW_CREATE:-false}" != true ]]; then
  echo "Foundation ownership is not initialized; recreate the disposable prototype first." >&2
  exit 1
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-foundation.XXXXXXXX")"
cleanup() {
  local status=$?
  rm -f -- "$work/template.json" "$work/parameters.json" || status=1
  rmdir "$work" || status=1
  exit "$status"
}
trap cleanup EXIT
az bicep build --file infra/foundation.bicep --stdout > "$work/template.json"
if ! jq -e '
  (.resources | type == "array" and length > 0) and
  ([.. | objects | select(((.type? // "") | ascii_downcase) == "microsoft.app/containerapps" or
                         ((.type? // "") | ascii_downcase) == "microsoft.app/jobs")] | length == 0)
' "$work/template.json" >/dev/null; then
  echo "The foundation template must not declare application containers or jobs." >&2
  exit 1
fi
jq -cS '.parameters' "$STACK_PARAMETERS" > "$work/parameters.json"
fingerprint="$(cat "$work/template.json" "$work/parameters.json" | openssl dgst -sha256 | awk '{print $NF}')"
if [[ "$exists" == true && "$(jq -r '.tags["pd-template-sha"] // ""' <<< "$current")" == "$fingerprint" ]]; then
  echo "Foundation infrastructure template and parameters are unchanged."
  exit 0
fi
echo "Applying the owned shared infrastructure. Removed managed resources and their child resources may be deleted."
az stack group create --resource-group "$AZURE_RESOURCE_GROUP" --name "$stack" \
  --template-file "$work/template.json" --parameters "@$STACK_PARAMETERS" \
  --deny-settings-mode none --action-on-unmanage deleteResources --resources-without-delete-support fail --yes \
  --tags "pd-owner=mortgage-prototype" "pd-target=foundation" "pd-template-sha=$fingerprint" --output none
created="$(az stack group show --resource-group "$AZURE_RESOURCE_GROUP" --name "$stack" --output json)"
if ! verify_stack <<< "$created"; then
  echo "Foundation stack ownership could not be verified after deployment; inspect Azure before retrying." >&2
  exit 1
fi
