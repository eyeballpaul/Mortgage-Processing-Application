#!/bin/bash
# Pause all Container Apps to minimize costs
# Usage: ./pause-apps.sh [resource-group]
# Default resource group: mortgage-demo-rg

set -e

RESOURCE_GROUP="${1:-mortgage-demo-rg}"
APPS=("mortgages-web" "staff-web" "mortgage-api" "products-api" "eligibility-api")

echo "🛑 Pausing all Container Apps in resource group: $RESOURCE_GROUP"
echo ""

for app in "${APPS[@]}"; do
  echo "⏸️  Pausing $app..."
  az containerapp replica count set \
    --resource-group "$RESOURCE_GROUP" \
    --name "$app" \
    --replicas 0 \
    --no-wait
done

echo ""
echo "✅ All apps paused! Cost is now minimal (~£1-2/month for storage only)"
echo ""
echo "To resume, run: ./resume-apps.sh $RESOURCE_GROUP"
