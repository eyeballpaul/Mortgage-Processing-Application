#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_SUBSCRIPTION_ID:?Missing AZURE_SUBSCRIPTION_ID}"
: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${STACK_SERVICE:?Missing STACK_SERVICE}"
: "${STACK_ACTION:?Missing STACK_ACTION}"

if [[ ! "$STACK_SERVICE" =~ ^[a-z][a-z0-9-]{0,40}$ ]]; then
  echo "Invalid stack service name." >&2
  exit 1
fi
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
stack="pd-app-$STACK_SERVICE"
if [[ "$STACK_SERVICE" == database-bootstrap ]]; then
  resource="$group/providers/Microsoft.App/jobs/mortgage-demo-env-bootstrap"
else
  resource="$group/providers/Microsoft.App/containerApps/$STACK_SERVICE"
fi
stacks="$(az stack group list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
if ! jq -e --arg name "$stack" '
  type == "array" and ([.[] | select(.name == $name)] | length <= 1)
' <<< "$stacks" >/dev/null; then
  echo "Invalid or ambiguous deployment stack listing." >&2
  exit 1
fi
exists="$(jq -r --arg name "$stack" 'any(.[]; .name == $name)' <<< "$stacks")"
if [[ "$exists" == true ]]; then
  current="$(az stack group show --resource-group "$AZURE_RESOURCE_GROUP" --name "$stack" --output json)"
  if ! jq -e --arg name "$stack" --arg group "$group" --arg target "$STACK_SERVICE" --arg resource "$resource" '
    (.id | type == "string" and ascii_downcase == (($group + "/providers/Microsoft.Resources/deploymentStacks/" + $name) | ascii_downcase)) and
    .tags["pd-owner"] == "mortgage-prototype" and .tags["pd-target"] == $target and
    (.resources | type == "array" and length == 1 and
      .[0].status == "managed" and
      (.[0].id | type == "string" and ascii_downcase == ($resource | ascii_downcase)))
  ' <<< "$current" >/dev/null; then
    echo "Stack $stack has an unexpected owner or managed resource; no stack changes were made." >&2
    exit 1
  fi
elif [[ "$STACK_ACTION" == apply && "${ALLOW_CREATE:-false}" != true ]]; then
  echo "Application stack ownership is not initialized; creation must be explicitly planned." >&2
  exit 1
fi

case "$STACK_ACTION" in
  check)
    if [[ "$exists" != true ]]; then
      echo "Stack $stack is missing." >&2
      exit 1
    fi
    ;;
  delete)
    if [[ "$STACK_SERVICE" == database-bootstrap ]]; then
      echo "The bootstrap stack must not be deleted as a removed application." >&2
      exit 1
    fi
    for service in "${app_services[@]}"; do
      if [[ "$service" == "$STACK_SERVICE" ]]; then
        echo "$STACK_SERVICE is still declared and cannot be deleted." >&2
        exit 1
      fi
    done
    if [[ "$exists" != true ]]; then
      echo "The removed application stack is absent; nothing to delete."
      exit 0
    fi
    echo "Deleting the owned stack and Container App $STACK_SERVICE."
    az stack group delete --resource-group "$AZURE_RESOURCE_GROUP" --name "$stack" \
      --action-on-unmanage deleteResources --yes --output none
    ;;
  apply)
    : "${STACK_PARAMETERS:?Missing STACK_PARAMETERS}"
    : "${STACK_IMAGE:?Missing STACK_IMAGE}"
    : "${AZURE_CONTAINER_REGISTRY:?Missing AZURE_CONTAINER_REGISTRY}"
    image="$(service_image "$STACK_SERVICE")"
    if [[ ! -f "$STACK_PARAMETERS" ]]; then
      echo "The stack parameter file is missing." >&2
      exit 1
    fi
    registry="$(az acr show --name "$AZURE_CONTAINER_REGISTRY" --resource-group "$AZURE_RESOURCE_GROUP" \
      --query loginServer --output tsv)"
    image_suffix="${STACK_IMAGE#"$registry/$image"}"
    if [[ ! "$registry" =~ ^[A-Za-z0-9][A-Za-z0-9.-]+$ ||
          "$STACK_IMAGE" != "$registry/$image"* ||
          ! "$image_suffix" =~ ^(:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}|@sha256:[0-9a-f]{64})$ ]]; then
      echo "The stack image does not belong to the expected registry and repository." >&2
      exit 1
    fi
    template="infra/apps/$STACK_SERVICE.bicep"
    if [[ ! -f "$template" ]]; then
      echo "The application template is missing for $STACK_SERVICE." >&2
      exit 1
    fi
    work="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-stack.XXXXXXXX")"
    cleanup() {
      local status=$?
      rm -f -- "$work/template.json" "$work/config.json" || status=1
      rmdir "$work" || status=1
      exit "$status"
    }
    trap cleanup EXIT
    az bicep build --file "$template" --stdout > "$work/template.json"
    if ! jq -e --arg service "$STACK_SERVICE" '
      (.resources | type == "array" and length == 1) and
      .resources[0].condition == null and .resources[0].copy == null and
      .resources[0].scope == null and
      (if $service == "database-bootstrap" then
        .resources[0].type == "Microsoft.App/jobs" and
        .resources[0].name == "[format('\''{0}-bootstrap'\'', parameters('\''environmentName'\''))]"
      elif $service == "notifications-worker" then
        .resources[0].type == "Microsoft.App/containerApps" and .resources[0].name == $service
      else
        .resources[0].type == "Microsoft.App/containerApps" and
        (.resources[0].name == "[parameters('\''appName'\'')]" or .resources[0].name == "[parameters('\''name'\'')]")
      end)
    ' "$work/template.json" >/dev/null; then
      echo "The application stack template must declare exactly its own resource." >&2
      exit 1
    fi
    if ! jq -e --arg service "$STACK_SERVICE" --arg image "$STACK_IMAGE" '
      .parameters.imageRef.value == $image and
      (if $service == "database-bootstrap" then .parameters.environmentName.value == "mortgage-demo-env"
       elif $service == "notifications-worker" then true
       else (.parameters.appName.value // .parameters.name.value) == $service end) and
      ((.parameters.appTags.value // .parameters.tags.value) |
        type == "object" and .application == "pd-mortgages-demo" and .managedBy == "bicep")
    ' "$STACK_PARAMETERS" >/dev/null; then
      echo "The stack parameters do not identify the expected application, image and tags." >&2
      exit 1
    fi
    if [[ "$STACK_SERVICE" == database-bootstrap ]]; then
      resource_name=mortgage-demo-env-bootstrap
      available="$(az containerapp job list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
    else
      resource_name="$STACK_SERVICE"
      available="$(az containerapp list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
    fi
    count="$(jq -er --arg name "$resource_name" \
      'if type == "array" then [.[] | select(.name == $name)] | length else error("Invalid resource list") end' <<< "$available")"
    if [[ "$count" == 0 ]]; then
      if [[ "${ALLOW_CREATE:-false}" != true ]]; then
        echo "$resource_name is missing; creation must be explicitly planned." >&2
        exit 1
      fi
    elif [[ "$count" == 1 ]]; then
      if [[ "$STACK_SERVICE" == database-bootstrap ]]; then
        app="$(az containerapp job show --resource-group "$AZURE_RESOURCE_GROUP" --name "$resource_name" --output json)"
      else
        app="$(az containerapp show --resource-group "$AZURE_RESOURCE_GROUP" --name "$resource_name" --output json)"
      fi
      if ! jq -e --arg id "$resource" --arg image "$STACK_IMAGE" --slurpfile parameters "$STACK_PARAMETERS" '
        (.id | type == "string" and ascii_downcase == ($id | ascii_downcase)) and
        (.tags == ($parameters[0].parameters.appTags.value // $parameters[0].parameters.tags.value)) and
        (.properties.template.containers | length == 1 and .[0].image == $image)
      ' <<< "$app" >/dev/null; then
        echo "$resource_name does not match the planned owned resource, tags and image." >&2
        exit 1
      fi
    else
      echo "Duplicate resources named $resource_name were returned." >&2
      exit 1
    fi
    jq -cS --slurpfile template "$work/template.json" '
      .parameters | to_entries | map(select(
        .key != "imageRef" and .key != "imageTag" and .key != "appTags" and .key != "tags" and
        (($template[0].parameters[.key].type // "" | ascii_downcase) != "securestring") and
        (($template[0].parameters[.key].type // "" | ascii_downcase) != "secureobject")
      )) | from_entries
    ' "$STACK_PARAMETERS" > "$work/config.json"
    secret_fingerprint=''
    if jq -e 'any(.parameters[]?; (.type | ascii_downcase) == "securestring" or
      (.type | ascii_downcase) == "secureobject")' "$work/template.json" >/dev/null; then
      if [[ -z "${DEPLOYMENT_FINGERPRINT_KEY:-}" ]]; then
        echo "Missing DEPLOYMENT_FINGERPRINT_KEY." >&2
        exit 1
      fi
      if [[ ! "$DEPLOYMENT_FINGERPRINT_KEY" =~ ^[0-9a-fA-F]{64}$ ]]; then
        echo "DEPLOYMENT_FINGERPRINT_KEY must be 32 random bytes encoded as hexadecimal." >&2
        exit 1
      fi
      secret_fingerprint="$(ruby -ropenssl -rjson - "$work/template.json" "$STACK_PARAMETERS" <<'RUBY'
template = JSON.parse(File.read(ARGV.fetch(0)))
parameters = JSON.parse(File.read(ARGV.fetch(1))).fetch('parameters')
names = template.fetch('parameters').keys.select do |name|
  %w[securestring secureobject].include?(template['parameters'][name].fetch('type').downcase)
end.sort
values = names.map { |name| [name, parameters.fetch(name).fetch('value')] }
key = [ENV.fetch('DEPLOYMENT_FINGERPRINT_KEY')].pack('H*')
print OpenSSL::HMAC.hexdigest('SHA256', key, JSON.generate(values))
RUBY
)"
    fi
    fingerprint="$({ cat "$work/template.json" "$work/config.json"; printf '%s' "$secret_fingerprint"; } | openssl dgst -sha256 | awk '{print $NF}')"
    if [[ "$exists" == true && "$(jq -r '.tags["pd-template-sha"] // ""' <<< "$current")" == "$fingerprint" ]]; then
      echo "$STACK_SERVICE infrastructure template is unchanged."
      exit 0
    fi
    echo "Applying the owned infrastructure for $STACK_SERVICE."
    az stack group create --resource-group "$AZURE_RESOURCE_GROUP" --name "$stack" \
      --template-file "$work/template.json" --parameters "@$STACK_PARAMETERS" \
      --deny-settings-mode none --action-on-unmanage deleteResources --resources-without-delete-support fail --yes \
      --tags "pd-owner=mortgage-prototype" "pd-target=$STACK_SERVICE" "pd-template-sha=$fingerprint" --output none
    created="$(az stack group show --resource-group "$AZURE_RESOURCE_GROUP" --name "$stack" --output json)"
    if ! jq -e --arg resource "$resource" --arg target "$STACK_SERVICE" '
      .tags["pd-owner"] == "mortgage-prototype" and .tags["pd-target"] == $target and
      (.resources | type == "array" and length == 1 and .[0].status == "managed" and
        (.[0].id | type == "string" and ascii_downcase == ($resource | ascii_downcase)))
    ' <<< "$created" >/dev/null; then
      echo "Stack ownership could not be verified after deployment; inspect Azure before retrying." >&2
      exit 1
    fi
    ;;
  *) echo "Invalid stack action." >&2; exit 1 ;;
esac
