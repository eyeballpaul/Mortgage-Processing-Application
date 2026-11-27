#!/usr/bin/env bash

app_services=(products-api eligibility-api mortgage-api notifications-worker mortgages-web staff-web)

bootstrap_fingerprint() {
  jq -Sc '{id, identity, properties: {
    environmentId: .properties.environmentId,
    configuration: .properties.configuration,
    template: .properties.template
  }}' | openssl dgst -sha256 | awk '{print $NF}'
}

service_project() {
  case "$1" in
    products-api) echo PD.Products.Api ;;
    eligibility-api) echo PD.Eligibility.Api ;;
    mortgage-api) echo PD.Mortgages.Api ;;
    mortgages-web) echo PD.Mortgages.Customer.Web ;;
    staff-web) echo PD.Mortgages.Staff.Web ;;
    notifications-worker) echo PD.Notifications.Worker ;;
    database-bootstrap) echo PD.Mortgages.DatabaseBootstrap ;;
    *) echo "Unknown release target: $1" >&2; return 1 ;;
  esac
}

service_image() {
  case "$1" in
    mortgage-api) echo mortgages-api ;;
    staff-web) echo mortgages-staff-web ;;
    products-api|eligibility-api|mortgages-web|notifications-worker|database-bootstrap) echo "$1" ;;
    *) echo "Unknown release target: $1" >&2; return 1 ;;
  esac
}

verify_mortgage_identity() {
  local app="$1" identity identity_id identity_client
  identity="$(az identity show --name mortgage-demo-env-mortgages --resource-group "$AZURE_RESOURCE_GROUP" --output json)"
  identity_id="$(jq -er '.id | select(type == "string" and length > 0)' <<< "$identity")"
  identity_client="$(jq -er '.clientId | select(type == "string" and length > 0)' <<< "$identity")"
  if ! jq -e --arg id "$identity_id" --arg client "$identity_client" '
    any((.identity.userAssignedIdentities // {}) | keys[]; ascii_downcase == ($id | ascii_downcase)) and
    any(.properties.template.containers[0].env[]?; .name == "AZURE_CLIENT_ID" and .value == $client) and
    any(.properties.template.containers[0].env[]?; .name == "Notifications__WorkerModel" and .value == "separate")
  ' <<< "$app" >/dev/null; then
    echo "Mortgage API identity or separate-worker configuration is not ready; follow the legacy cutover guide first." >&2
    return 1
  fi
}
