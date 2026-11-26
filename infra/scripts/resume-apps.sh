#!/bin/bash
# Resume all Container Apps
# Usage: ./resume-apps.sh [resource-group]
# Default resource group: mortgage-demo-rg

set -e

RESOURCE_GROUP="${1:-mortgage-demo-rg}"
APPS=("mortgages-web" "staff-web" "mortgage-api" "products-api" "eligibility-api")
MIN_REPLICAS=1
MAX_REPLICAS=2

echo "🚀 Resuming all Container Apps in resource group: $RESOURCE_GROUP"
echo ""

for app in "${APPS[@]}"; do
  echo "▶️  Resuming $app (min: $MIN_REPLICAS, max: $MAX_REPLICAS)..."
  az containerapp replica count set \
    --resource-group "$RESOURCE_GROUP" \
    --name "$app" \
    --replicas 1 \
    --no-wait
done

echo ""
echo "⏳ Apps are starting. This typically takes 1-2 minutes..."
echo "✅ All apps resumed!"
echo ""
echo "To pause, run: ./pause-apps.sh $RESOURCE_GROUP"
