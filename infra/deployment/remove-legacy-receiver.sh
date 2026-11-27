#!/usr/bin/env bash
set -euo pipefail

principal="${1:?Missing mortgage identity principal ID}"
queue="${2:?Missing notification queue ID}"
receiver=4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0
owner=090c5cfd-751d-490a-894a-3ce6f1109419
if [[ ! "$principal" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ || ! "$queue" =~ ^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft.ServiceBus/namespaces/[^/]+/queues/submission-notifications$ ]]; then
  echo "Invalid mortgage identity or notification queue; refusing role cleanup." >&2
  exit 1
fi

list_assignments() {
  az role assignment list --assignee-object-id "$principal" --scope "$queue" \
    --include-inherited --output json
}

assignments="$(list_assignments)"
if ! jq -e 'type == "array" and all(.[];
  (.id | type == "string") and (.scope | type == "string") and
  (.principalId | type == "string") and (.roleDefinitionId | type == "string"))' <<< "$assignments" >/dev/null; then
  echo "Invalid role-assignment listing; refusing notification cutover." >&2
  exit 1
fi
if jq -e --arg principal "$principal" --arg queue "$queue" --arg receiver "$receiver" --arg owner "$owner" '
  any(.[];
    (.principalId | ascii_downcase) == ($principal | ascii_downcase) and
    ((.roleDefinitionId | ascii_downcase | endswith("/" + $owner)) or
     ((.roleDefinitionId | ascii_downcase | endswith("/" + $receiver)) and
      (.scope | ascii_downcase) != ($queue | ascii_downcase))))
' <<< "$assignments" >/dev/null; then
  echo "Mortgage identity has broader inherited Service Bus receive/owner access. Review and remove it before cutover." >&2
  exit 1
fi

while IFS= read -r assignment; do
  if ! jq -en --arg id "$assignment" --arg prefix "$queue/providers/Microsoft.Authorization/roleAssignments/" '
    ($id | ascii_downcase) as $actual |
    ($prefix | ascii_downcase) as $expected |
    ($actual | startswith($expected)) and
    ($actual | ltrimstr($expected) | test("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"))
  ' >/dev/null; then
    echo "Unexpected legacy role-assignment ID; refusing to remove it." >&2
    exit 1
  fi
  echo "Removing the mortgage API's legacy queue receiver assignment."
  az role assignment delete --ids "$assignment"
done < <(jq -r --arg principal "$principal" --arg queue "$queue" --arg receiver "$receiver" '
  .[] | select((.principalId | ascii_downcase) == ($principal | ascii_downcase) and
    (.scope | ascii_downcase) == ($queue | ascii_downcase) and
    (.roleDefinitionId | ascii_downcase | endswith("/" + $receiver))) | .id
' <<< "$assignments")

for ((attempt=1; attempt<=6; attempt++)); do
  assignments="$(list_assignments)"
  if jq -e --arg principal "$principal" --arg receiver "$receiver" --arg owner "$owner" '
    type == "array" and all(.[];
      (.principalId | type == "string") and (.roleDefinitionId | type == "string") and
      ((.principalId | ascii_downcase) != ($principal | ascii_downcase) or
       (((.roleDefinitionId | ascii_downcase | endswith("/" + $receiver)) or
         (.roleDefinitionId | ascii_downcase | endswith("/" + $owner))) | not)))
  ' <<< "$assignments" >/dev/null; then
    exit 0
  fi
  sleep 5
done
echo "Could not verify removal of the mortgage identity's receiver access." >&2
exit 1
