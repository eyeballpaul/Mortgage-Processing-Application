#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."

: "${AZURE_RESOURCE_GROUP:?Set AZURE_RESOURCE_GROUP to the dedicated prototype resource group}"
: "${AZURE_CLIENT_ID:?Set AZURE_CLIENT_ID to the GitHub deployment application client ID}"
: "${DEPLOYMENT_FINGERPRINT_KEY:?Set DEPLOYMENT_FINGERPRINT_KEY to the stable 32-byte key}"

# Restore the constrained group role-assignment permission after a fresh group was created.
bash infra/setup-deployment-permissions.sh

# Add the notification worker and scaler principals to the existing restricted assignment.
bash infra/setup-notification-permissions.sh

# Adopt the freshly deployed foundation and app resources into deployment stacks.
bash infra/deployment/initialize-stacks.sh

echo "Prototype Azure permissions and deployment-stack ownership are ready."
