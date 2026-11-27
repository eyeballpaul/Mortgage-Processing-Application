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
: "${STACK_TARGET:?Missing STACK_TARGET}"
: "${STACK_PARAMETERS:?Missing STACK_PARAMETERS}"
if [[ "$STACK_TARGET" != foundation ]]; then
  : "${STACK_IMAGE:?Missing STACK_IMAGE}"
  : "${MORTGAGE_API_CLIENT_ID:?Missing MORTGAGE_API_CLIENT_ID}"
  : "${STAFF_CLIENT_ID:?Missing STAFF_CLIENT_ID}"
  : "${STAFF_CLIENT_SECRET:?Missing STAFF_CLIENT_SECRET}"
  : "${PRODUCTS_API_KEY:?Missing PRODUCTS_API_KEY}"
  : "${ELIGIBILITY_API_KEY:?Missing ELIGIBILITY_API_KEY}"
fi

case "${OTLP_PROTOCOL:-grpc}" in
  grpc|http/protobuf) ;;
  *) echo "OTLP_PROTOCOL must be grpc or http/protobuf." >&2; exit 1 ;;
esac

if [[ "$(az account show --query id --output tsv)" != "$AZURE_SUBSCRIPTION_ID" ]]; then
  echo "Selected Azure subscription does not match the configured subscription." >&2
  exit 1
fi
group="$(az group show --name "$AZURE_RESOURCE_GROUP" --output json)"
group_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$group")"
location="$(jq -er '.location | select(type == "string" and length > 0)' <<< "$group")"
expected_group="/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$AZURE_RESOURCE_GROUP"
if ! jq -en --arg group "$group_id" --arg expected "$expected_group" \
  '($group | ascii_downcase) == ($expected | ascii_downcase)' >/dev/null; then
  echo "Resource group does not match the configured subscription." >&2
  exit 1
fi

umask 077
work="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-stack-parameters.XXXXXXXX")"
output_tmp=''
cleanup() {
  local status=$?
  if [[ -n "$output_tmp" ]]; then rm -f -- "$output_tmp" || status=1; fi
  rm -f -- "$work/template.json" "$work/config.json" || status=1
  rmdir "$work" || status=1
  exit "$status"
}
trap cleanup EXIT

if [[ "$STACK_TARGET" == foundation ]]; then
  principal="$(az ad sp show --id "$AZURE_CLIENT_ID" --query id --output tsv)"
  if [[ ! "$principal" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    echo "The deployment principal object ID could not be resolved." >&2
    exit 1
  fi
  servers="$(az sql server list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  sql_count="$(jq -er --arg name "$AZURE_SQL_SERVER" '
    if type == "array" then [.[] | select(.name == $name)] | length
    else error("Invalid SQL server listing") end' <<< "$servers")"
  if [[ "$sql_count" -gt 1 ]]; then
    echo "Duplicate SQL servers were returned." >&2
    exit 1
  fi
  create_sql=false
  if [[ "$sql_count" == 0 ]]; then create_sql=true; fi
  jq -n --arg location "$location" --arg principal "$principal" \
    --arg registry "$AZURE_CONTAINER_REGISTRY" --arg sql "$AZURE_SQL_SERVER" \
    --arg storage "$AZURE_STORAGE_ACCOUNT" --argjson create_sql "$create_sql" '{
      location:$location, containerRegistryName:$registry,
      containerAppEnvironmentName:"mortgage-demo-env", sqlServerName:$sql,
      documentStorageAccountName:$storage, pipelinePrincipalId:$principal,
      pipelinePrincipalType:"ServicePrincipal", createSqlServer:$create_sql,
      deployAlerts:true, alertEmail:(env.AZURE_ALERT_EMAIL // "")
    }' > "$work/config.json"
  template=infra/foundation.bicep
else
  service_image "$STACK_TARGET" >/dev/null
  template="infra/apps/$STACK_TARGET.bicep"
  if [[ ! -f "$template" ]]; then
    echo "The application template is missing for $STACK_TARGET." >&2
    exit 1
  fi
  registry="$(az acr show --name "$AZURE_CONTAINER_REGISTRY" --resource-group "$AZURE_RESOURCE_GROUP" \
    --query loginServer --output tsv)"
  if [[ ! "$registry" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*\.[A-Za-z0-9.-]+$ ]]; then
    echo "The registry returned an invalid login server." >&2
    exit 1
  fi
  identities="$(az identity list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  if ! jq -e --arg group "$group_id" '
    type == "array" and all(.[];
      (.name | type == "string" and length > 0) and
      (.clientId | type == "string" and length > 0) and
      (.name as $name | .id | type == "string" and
        ascii_downcase == (($group + "/providers/Microsoft.ManagedIdentity/userAssignedIdentities/" + $name) | ascii_downcase)))
  ' <<< "$identities" >/dev/null; then
    echo "The managed identity listing is invalid or out of scope." >&2
    exit 1
  fi
  sql_fqdn=''
  if [[ "$STACK_TARGET" == products-api || "$STACK_TARGET" == mortgage-api ||
        "$STACK_TARGET" == notifications-worker || "$STACK_TARGET" == mortgages-web ||
        "$STACK_TARGET" == database-bootstrap ]]; then
    sql="$(az sql server show --name "$AZURE_SQL_SERVER" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
    sql_fqdn="$(jq -er '.fullyQualifiedDomainName | select(type == "string" and length > 0)' <<< "$sql")"
  fi
  servicebus_name=''
  servicebus_fqdn=''
  if [[ "$STACK_TARGET" == mortgage-api || "$STACK_TARGET" == notifications-worker ]]; then
    namespaces="$(az servicebus namespace list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
    namespace="$(jq -cer '
      [ .[] | select(.name | type == "string" and test("^mortgage-sb-[a-z0-9]{13}$")) ] |
      if length == 1 then .[0] else error("Expected one prototype Service Bus namespace") end
    ' <<< "$namespaces")"
    servicebus_name="$(jq -er '.name' <<< "$namespace")"
    servicebus_fqdn="$(jq -er '.serviceBusEndpoint | select(type == "string" and startswith("https://")) | ltrimstr("https://") | rtrimstr("/") | split(":")[0]' <<< "$namespace")"
  fi

  products_fqdn=''
  eligibility_fqdn=''
  mortgage_fqdn=''
  if [[ "$STACK_TARGET" == mortgage-api ]]; then
    products_fqdn="$(az containerapp show --resource-group "$AZURE_RESOURCE_GROUP" --name products-api \
      --query properties.configuration.ingress.fqdn --output tsv)"
    eligibility_fqdn="$(az containerapp show --resource-group "$AZURE_RESOURCE_GROUP" --name eligibility-api \
      --query properties.configuration.ingress.fqdn --output tsv)"
  elif [[ "$STACK_TARGET" == staff-web || "$STACK_TARGET" == mortgages-web ]]; then
    mortgage_fqdn="$(az containerapp show --resource-group "$AZURE_RESOURCE_GROUP" --name mortgage-api \
      --query properties.configuration.ingress.fqdn --output tsv)"
  fi
  for hostname in "$sql_fqdn" "$servicebus_fqdn" "$products_fqdn" "$eligibility_fqdn" "$mortgage_fqdn"; do
    if [[ -n "$hostname" && ! "$hostname" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*\.[A-Za-z0-9.-]+$ ]]; then
      echo "A dependency returned an invalid hostname." >&2
      exit 1
    fi
  done
  if [[ "$STACK_TARGET" == database-bootstrap ]]; then
    app_name=mortgage-demo-env-bootstrap
    available="$(az containerapp job list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  else
    app_name="$STACK_TARGET"
    available="$(az containerapp list --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  fi
  count="$(jq -er --arg name "$app_name" \
    'if type == "array" then [.[] | select(.name == $name)] | length else error("Invalid resource list") end' <<< "$available")"
  if [[ "$count" == 0 ]]; then
    tags='{"application":"pd-mortgages-demo","managedBy":"bicep"}'
  elif [[ "$count" == 1 ]]; then
    if [[ "$STACK_TARGET" == database-bootstrap ]]; then
      app="$(az containerapp job show --resource-group "$AZURE_RESOURCE_GROUP" --name "$app_name" --output json)"
    else
      app="$(az containerapp show --resource-group "$AZURE_RESOURCE_GROUP" --name "$app_name" --output json)"
    fi
    tags="$(jq -ce '.tags | select(type == "object" and .application == "pd-mortgages-demo" and .managedBy == "bicep")' <<< "$app")"
  else
    echo "Duplicate resources named $app_name were returned." >&2
    exit 1
  fi
  jq -n --argjson identities "$identities" --argjson tags "$tags" --arg group "$group_id" \
    --arg registry "$registry" --arg sql "$sql_fqdn" --arg sb_name "$servicebus_name" \
    --arg sb_fqdn "$servicebus_fqdn" --arg target "$STACK_TARGET" --arg image "$STACK_IMAGE" \
    --arg location "$location" --arg products_fqdn "$products_fqdn" \
    --arg eligibility_fqdn "$eligibility_fqdn" --arg mortgage_fqdn "$mortgage_fqdn" '
    def identity($suffix):
      [ $identities[] | select(.name == ("mortgage-demo-env-" + $suffix)) ] |
      if length == 1 then .[0] elif length == 0 then null else error("Managed identity is ambiguous: " + $suffix) end;
    {
      registryServer:$registry, pullIdentityId:identity("pull").id,
      environmentId:($group + "/providers/Microsoft.App/managedEnvironments/mortgage-demo-env"),
      mortgagesIdentityClientId:identity("mortgages").clientId,
      productsIdentityClientId:identity("products").clientId,
      customerSessionsIdentityClientId:identity("customer-sessions").clientId,
      notificationsIdentityClientId:identity("notifications").clientId,
      bootstrapIdentityClientId:identity("bootstrap").clientId,
      staffIdentityClientId:identity("staff").clientId,
      sqlServerFqdn:$sql, serviceBusFqdn:$sb_fqdn, serviceBusName:$sb_name,
      serviceBusQueueName:"submission-notifications",
      productsApiFqdn:$products_fqdn, eligibilityApiFqdn:$eligibility_fqdn,
      mortgageApiFqdn:$mortgage_fqdn,
      mortgagesIdentityName:identity("mortgages").name,
      productsIdentityName:identity("products").name,
      customerSessionsIdentityName:identity("customer-sessions").name,
      notificationsIdentityName:identity("notifications").name,
      notificationsScalerIdentityId:identity("notifications-scaler").id,
      notificationsIdentityId:identity("notifications").id,
      customerSessionsIdentityId:identity("customer-sessions").id,
      mortgagesIdentityId:identity("mortgages").id,
      productsIdentityId:identity("products").id,
      bootstrapIdentityId:identity("bootstrap").id,
      staffIdentityId:identity("staff").id,
      storageName:env.AZURE_STORAGE_ACCOUNT, appName:$target, name:$target,
      mortgageApiClientId:env.MORTGAGE_API_CLIENT_ID, staffClientId:env.STAFF_CLIENT_ID,
      staffClientSecret:env.STAFF_CLIENT_SECRET, productServiceApiKey:env.PRODUCTS_API_KEY,
      eligibilityServiceApiKey:env.ELIGIBILITY_API_KEY,
      environmentName:"mortgage-demo-env", otlpEndpoint:(env.OTLP_ENDPOINT // ""),
      otlpProtocol:(env.OTLP_PROTOCOL | if . == null or . == "" then "grpc" else . end),
      imageTag:"latest", imageRef:$image,
      appTags:$tags, tags:$tags, location:$location
    }' > "$work/config.json"
fi
az bicep build --file "$template" --stdout > "$work/template.json"
output_tmp="$(mktemp "${STACK_PARAMETERS}.tmp.XXXXXXXX")"
jq -n --slurpfile template "$work/template.json" --slurpfile config "$work/config.json" '
  $template[0].parameters as $parameters | $config[0] as $config |
  ([ $parameters | to_entries[] | select(.value.defaultValue == null and
      ($config[.key] == null or ($config[.key] == "" and .key != "otlpEndpoint"))) | .key ]) as $missing |
  if ($missing | length) != 0 then error("Missing required stack inputs: " + ($missing | join(", ")))
  else {parameters: ($parameters | with_entries(
    select($config[.key] != null) | .value = {value: $config[.key]}
  ))} end
' > "$output_tmp"
mv -f -- "$output_tmp" "$STACK_PARAMETERS"
output_tmp=''
