#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source infra/deployment/services.sh

: "${AZURE_RESOURCE_GROUP:?Missing AZURE_RESOURCE_GROUP}"
: "${DEPLOYMENT_FINGERPRINT_KEY:?Missing DEPLOYMENT_FINGERPRINT_KEY}"

# Keep generated stack parameters private because they contain deployment secrets.
umask 077
work="$(mktemp -d "${TMPDIR:-/tmp}/mortgage-init-stacks.XXXXXXXX")"
cleanup() {
  local status=$?
  rm -f -- "$work/parameters.json" || status=1
  rmdir "$work" || status=1
  exit "$status"
}
trap cleanup EXIT
# Adopt the already deployed foundation and resources so future changes are stack-managed.
export STACK_PARAMETERS="$work/parameters.json"
STACK_TARGET=foundation bash infra/deployment/stack-parameters.sh
ALLOW_CREATE=true bash infra/deployment/foundation-stack.sh

# Initialize each current image as the baseline for later selective deployments.
for service in database-bootstrap "${app_services[@]}"; do
  if [[ "$service" == database-bootstrap ]]; then
    app="$(az containerapp job show --resource-group "$AZURE_RESOURCE_GROUP" \
      --name mortgage-demo-env-bootstrap --output json)"
  else
    app="$(az containerapp show --resource-group "$AZURE_RESOURCE_GROUP" --name "$service" --output json)"
  fi
  image="$(jq -er '.properties.template.containers | select(length == 1) |
    .[0].image | select(type == "string" and length > 0)' <<< "$app")"
  STACK_TARGET="$service" STACK_IMAGE="$image" bash infra/deployment/stack-parameters.sh
  STACK_SERVICE="$service" STACK_ACTION=apply STACK_IMAGE="$image" ALLOW_CREATE=true \
    bash infra/deployment/app-stack.sh
done
echo "Prototype deployment stack ownership initialized."
