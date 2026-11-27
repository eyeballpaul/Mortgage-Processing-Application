#!/usr/bin/env bash
set -euo pipefail

: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${STAFF_CLIENT_ID:?Missing STAFF_CLIENT_ID}"
action="${STAFF_CALLBACK_ACTION:-add}"
if [[ "$action" != add && "$action" != remove ]]; then
  echo "Invalid staff callback action." >&2
  exit 1
fi

app="$(az containerapp show --name staff-web --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
if ! jq -e --arg client "$STAFF_CLIENT_ID" '
  any(.properties.template.containers[0].env[]?; .name == "AzureAd__ClientId" and .value == $client)
' <<< "$app" >/dev/null; then
  echo "The deployed staff app does not match STAFF_CLIENT_ID." >&2
  exit 1
fi
hostname="$(jq -er '.properties.configuration.ingress.fqdn | select(type == "string" and length > 0)' <<< "$app")"
if [[ ! "$hostname" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]; then
  echo "The staff app returned an invalid hostname." >&2
  exit 1
fi
signin="https://$hostname/signin-oidc"
signout="https://$hostname/signout-callback-oidc"
registration="$(az ad app show --id "$STAFF_CLIENT_ID" --output json)"
object_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$registration")"
update="$(jq -ce --arg signin "$signin" --arg signout "$signout" --arg action "$action" '
  (.web.redirectUris // []) as $uris |
  if ($uris | type == "array" and all(.[]; type == "string")) and
    ((.web.redirectUriSettings // []) | type == "array" and all(.[]; .index == null)) then
    {web: (((.web // {}) | del(.redirectUriSettings)) + {redirectUris:
      (if $action == "add" then $uris + [$signin, $signout] | unique
       else $uris | map(select(. != $signin and . != $signout)) end)})}
  else error("Invalid or indexed staff redirect settings") end
' <<< "$registration")"
if ! jq -e --arg signin "$signin" --arg signout "$signout" --arg action "$action" '
  (.web.redirectUris // []) as $uris |
  if $action == "add" then ($uris | index($signin)) != null and ($uris | index($signout)) != null
  else ($uris | index($signin)) == null and ($uris | index($signout)) == null end
' <<< "$registration" >/dev/null; then
  az rest --method patch --url "https://graph.microsoft.com/v1.0/applications/$object_id" \
    --body "$update" --output none
  verified=false
  for ((attempt=1; attempt<=6; attempt++)); do
    after="$(az ad app show --id "$STAFF_CLIENT_ID" --output json)"
    if jq -e --argjson before "$registration" --arg signin "$signin" --arg signout "$signout" --arg action "$action" '
      .web as $web | (.web.redirectUris // []) as $uris |
      (if $action == "add" then ($uris | index($signin)) != null and ($uris | index($signout)) != null
       else ($uris | index($signin)) == null and ($uris | index($signout)) == null end) and
      all(($before.web.redirectUris // [])[] | select($action == "add" or (. != $signin and . != $signout));
        . as $old | ($uris | index($old)) != null) and
      all((($before.web // {}) | to_entries[] | select(.key != "redirectUris" and .key != "redirectUriSettings"));
        . as $entry | $web[$entry.key] == $entry.value)
    ' <<< "$after" >/dev/null; then
      verified=true
      break
    fi
    sleep 5
  done
  if [[ "$verified" != true ]]; then
    echo "Staff callback update could not be verified; inspect the app registration." >&2
    exit 1
  fi
fi
