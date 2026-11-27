#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${AZURE_RESOURCE_GROUP:-}" || -z "${AZURE_CONTAINER_REGISTRY:-}" || -z "${GITHUB_SHA:-}" ]]; then
  echo "AZURE_RESOURCE_GROUP, AZURE_CONTAINER_REGISTRY and GITHUB_SHA are required." >&2
  exit 1
fi
if [[ ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ]]; then
  echo "GITHUB_SHA must be a full Git commit SHA." >&2
  exit 1
fi

if [[ "$(az group exists --name "$AZURE_RESOURCE_GROUP" --output tsv)" != true ]]; then
  echo "Resource group does not exist; create it with the manual quickstart first." >&2
  exit 1
fi
registry_server="$(az acr show --name "$AZURE_CONTAINER_REGISTRY" --resource-group "$AZURE_RESOURCE_GROUP" --query loginServer --output tsv)"
if [[ -z "$registry_server" || "$registry_server" == "None" ]]; then
  echo "The existing registry was not found." >&2
  exit 1
fi

app_names=(products-api eligibility-api mortgage-api mortgages-web staff-web notifications-worker)
image_names=(products-api eligibility-api mortgages-api mortgages-web mortgages-staff-web notifications-worker)
project_names=(PD.Products.Api PD.Eligibility.Api PD.Mortgages.Api PD.Mortgages.Customer.Web PD.Mortgages.Staff.Web PD.Notifications.Worker)
job_name=mortgage-demo-env-bootstrap
job_json="$(az containerapp job show --name "$job_name" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
job_id="$(jq -er '.id' <<< "$job_json")"
job_baseline="$(jq -r '.tags["pd-bootstrap-commit"] // ""' <<< "$job_json")"

needs_build() {
  local project="$1" baseline="$2"
  if [[ "$baseline" =~ ^[0-9a-f]{40}$ && "$baseline" != "$GITHUB_SHA" ]] &&
     git merge-base --is-ancestor "$GITHUB_SHA" "$baseline"; then
    echo "Refusing to deploy an older commit over the deployed $project version." >&2
    return 1
  fi
  if [[ ! "$baseline" =~ ^[0-9a-f]{40}$ ]] || ! git merge-base --is-ancestor "$baseline" "$GITHUB_SHA"; then
    echo true
    return
  fi
  python3 infra/deployment/affected.py \
    --project "$project/$project.csproj" --base "$baseline" --head "$GITHUB_SHA"
}

app_changes=()
app_ids=()
notification_settings_current=false
identity_name=mortgage-demo-env-mortgages
identity_json="$(az identity show --name "$identity_name" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
identity_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$identity_json")"
identity_client_id="$(jq -er '.clientId | select(type == "string" and length > 0)' <<< "$identity_json")"
identity_principal_id="$(jq -er '.principalId | select(type == "string" and length > 0)' <<< "$identity_json")"
for ((i=0; i<${#app_names[@]}; i++)); do
  app_json="$(az containerapp show --name "${app_names[$i]}" --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  app_ids+=("$(jq -er '.id' <<< "$app_json")")
  if [[ "${app_names[$i]}" == mortgage-api ]]; then
    if ! jq -e --arg id "$identity_id" --arg client "$identity_client_id" '
      any((.identity.userAssignedIdentities // {}) | keys[]; ascii_downcase == ($id | ascii_downcase)) and
      any(.properties.template.containers[0].env[]?; .name == "AZURE_CLIENT_ID" and .value == $client)
    ' <<< "$app_json" >/dev/null; then
      echo "Mortgage API must use the expected managed identity and AZURE_CLIENT_ID before enabling notifications." >&2
      exit 1
    fi
    mortgage_app_json="$app_json"
    if ! jq -e 'any(.properties.template.containers[0].env[]?;
      .name == "Notifications__WorkerModel" and .value == "separate")' <<< "$app_json" >/dev/null; then
      echo "Legacy mortgage-api consumer detected. Follow the legacy cutover instructions before deploying changes." >&2
      exit 1
    fi
  fi
  image="$(jq -er '.properties.template.containers[0].image' <<< "$app_json")"
  deployed_commit="$(jq -r '.tags["pd-deployed-commit"] // ""' <<< "$app_json")"
  prefix="$registry_server/${image_names[$i]}:"
  if [[ "$image" == "$prefix"* && "${image#"$prefix"}" == "$deployed_commit" ]]; then
    baseline="${image#"$prefix"}"
  else
    baseline=""
  fi
  app_changes+=("$(needs_build "${project_names[$i]}" "$baseline")")
done
bootstrap_changed="$(needs_build PD.Mortgages.DatabaseBootstrap "$job_baseline")"

for ((i=0; i<${#app_names[@]}; i++)); do
  if [[ "${app_changes[$i]}" == true ]]; then
    echo "Building ${image_names[$i]}..."
    az acr build --registry "$AZURE_CONTAINER_REGISTRY" \
      --image "${image_names[$i]}:$GITHUB_SHA" --file "${project_names[$i]}/Dockerfile" .
  fi
done

if [[ "$bootstrap_changed" == true ]]; then
  echo "Building and running the database bootstrap..."
  az acr build --registry "$AZURE_CONTAINER_REGISTRY" --image "database-bootstrap:$GITHUB_SHA" \
    --file PD.Mortgages.DatabaseBootstrap/Dockerfile .
  az containerapp job update --name "$job_name" --resource-group "$AZURE_RESOURCE_GROUP" \
    --image "$registry_server/database-bootstrap:$GITHUB_SHA" --output none
  execution="$(az containerapp job start --name "$job_name" --resource-group "$AZURE_RESOURCE_GROUP" --query name --output tsv)"
  if [[ -z "$execution" || "$execution" == "None" ]]; then
    echo "The bootstrap job did not return an execution name." >&2
    exit 1
  fi
  succeeded=false
  for ((attempt=1; attempt<=120; attempt++)); do
    status="$(az containerapp job execution show --name "$job_name" --resource-group "$AZURE_RESOURCE_GROUP" \
      --job-execution-name "$execution" --query properties.status --output tsv)"
    case "$status" in
      Succeeded) succeeded=true; break ;;
      Failed|Stopped|Degraded)
        echo "Database bootstrap $execution ended with status $status; inspect its logs." >&2
        exit 1 ;;
    esac
    sleep 15
  done
  if [[ "$succeeded" != true ]]; then
    echo "Database bootstrap $execution timed out; inspect it before retrying." >&2
    exit 1
  fi
  az tag update --resource-id "$job_id" --operation merge \
    --tags "pd-bootstrap-commit=$GITHUB_SHA" --output none
fi

echo "Deploying the Service Bus notification queue and permissions..."
notification_outputs="$(az deployment group create --name mortgage-notifications-pipeline \
  --resource-group "$AZURE_RESOURCE_GROUP" --template-file infra/notifications.bicep \
  --parameters "mortgagesIdentityName=$identity_name" \
  "notificationsIdentityName=mortgage-demo-env-notifications" \
  "notificationsScalerIdentityName=mortgage-demo-env-notifications-scaler" \
  --query properties.outputs --output json)"
service_bus_namespace="$(jq -er '.fullyQualifiedNamespace.value | select(type == "string" and length > 0)' <<< "$notification_outputs")"
queue_name="$(jq -er '.queueName.value | select(type == "string" and length > 0)' <<< "$notification_outputs")"
queue_id="$(jq -er '.queueId.value | select(type == "string" and length > 0)' <<< "$notification_outputs")"
if [[ ! "$service_bus_namespace" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*\.[A-Za-z0-9.-]+$ ||
      ! "$queue_name" =~ ^[A-Za-z0-9][A-Za-z0-9._/-]*$ ]]; then
  echo "Notification deployment returned an invalid namespace or queue name." >&2
  exit 1
fi
bash infra/deployment/remove-legacy-receiver.sh "$identity_principal_id" "$queue_id"
if jq -e --arg namespace "$service_bus_namespace" --arg queue "$queue_name" '
  [.properties.template.containers[0].env[]?] as $env |
  any($env[]; .name == "Notifications__Enabled" and .value == "true") and
  any($env[]; .name == "Notifications__ServiceBusNamespace" and .value == $namespace) and
  any($env[]; .name == "Notifications__QueueName" and .value == $queue)
' <<< "$mortgage_app_json" >/dev/null; then
  notification_settings_current=true
fi

deployed=false
for ((i=0; i<${#app_names[@]}; i++)); do
  if [[ "${app_names[$i]}" == mortgage-api && "$notification_settings_current" != true ]]; then
    echo "Updating mortgage-api notification configuration..."
    if [[ "${app_changes[$i]}" == true ]]; then
      az containerapp update --name mortgage-api --resource-group "$AZURE_RESOURCE_GROUP" \
        --image "$registry_server/${image_names[$i]}:$GITHUB_SHA" \
        --set-env-vars Notifications__Enabled=true \
        "Notifications__ServiceBusNamespace=$service_bus_namespace" \
        "Notifications__QueueName=$queue_name" --output none
    else
      az containerapp update --name mortgage-api --resource-group "$AZURE_RESOURCE_GROUP" \
        --set-env-vars Notifications__Enabled=true \
        "Notifications__ServiceBusNamespace=$service_bus_namespace" \
        "Notifications__QueueName=$queue_name" --output none
    fi
    deployed=true
  elif [[ "${app_changes[$i]}" == true ]]; then
    echo "Updating ${app_names[$i]}..."
    az containerapp update --name "${app_names[$i]}" --resource-group "$AZURE_RESOURCE_GROUP" \
      --image "$registry_server/${image_names[$i]}:$GITHUB_SHA" --output none
    deployed=true
  fi
done

if [[ "$deployed" != true && "$bootstrap_changed" != true ]]; then
  echo "No application changes to deploy."
  exit 0
fi

for app in mortgages-web staff-web; do
  hostname="$(az containerapp show --name "$app" --resource-group "$AZURE_RESOURCE_GROUP" \
    --query properties.configuration.ingress.fqdn --output tsv)"
  if [[ -z "$hostname" || "$hostname" == "None" ]]; then
    echo "$app has no ingress hostname." >&2
    exit 1
  fi
  ready=false
  for ((attempt=1; attempt<=60; attempt++)); do
    if curl --silent --show-error --fail --max-time 10 "https://$hostname/health/ready" >/dev/null; then
      ready=true
      break
    fi
    sleep 10
  done
  if [[ "$ready" != true ]]; then
    echo "$app failed its readiness check; inspect Azure logs." >&2
    exit 1
  fi
  if [[ "$app" == mortgages-web ]]; then
    customer_hostname="$hostname"
  fi
done
dependencies_ready=false
for ((attempt=1; attempt<=12; attempt++)); do
  if curl --silent --show-error --fail --max-time 30 "https://$customer_hostname/health/dependencies" >/dev/null; then
    dependencies_ready=true
    break
  fi
  sleep 10
done
if [[ "$dependencies_ready" != true ]]; then
  echo "Products or Eligibility dependency check failed; inspect Azure logs." >&2
  exit 1
fi
for ((i=0; i<${#app_names[@]}; i++)); do
  if [[ "${app_changes[$i]}" == true ]]; then
    az tag update --resource-id "${app_ids[$i]}" --operation merge \
      --tags "pd-deployed-commit=$GITHUB_SHA" --output none
  fi
done
echo "Deployment, public readiness and dependency checks completed."
