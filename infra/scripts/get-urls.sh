#!/bin/bash
# Get the public URLs for the customer and staff apps
# Usage: ./get-urls.sh [resource-group]
# Default resource group: mortgage-demo-rg

RESOURCE_GROUP="${1:-mortgage-demo-rg}"
DEPLOYMENT_NAME="${2:-main}"

echo "🔗 Application URLs"
echo "=================="
echo ""

# Check if resource group exists
if ! az group exists --name "$RESOURCE_GROUP" -o tsv | grep -q "true"; then
  echo "❌ Resource group '$RESOURCE_GROUP' does not exist"
  exit 1
fi

# Try to get from deployment outputs
if az deployment group show -n "$DEPLOYMENT_NAME" -g "$RESOURCE_GROUP" &>/dev/null; then
  echo "✅ Retrieving URLs from deployment..."
  echo ""
  
  CUSTOMER_URL=$(az deployment group show -n "$DEPLOYMENT_NAME" -g "$RESOURCE_GROUP" --query 'properties.outputs.mortgagesWebUrl.value' -o tsv)
  STAFF_URL=$(az deployment group show -n "$DEPLOYMENT_NAME" -g "$RESOURCE_GROUP" --query 'properties.outputs.staffWebUrl.value' -o tsv)
  
  if [ -n "$CUSTOMER_URL" ] && [ "$CUSTOMER_URL" != "null" ]; then
    echo "👤 Customer App:"
    echo "   https://$CUSTOMER_URL"
  else
    echo "⚠️  Customer App URL not available"
  fi
  
  if [ -n "$STAFF_URL" ] && [ "$STAFF_URL" != "null" ]; then
    echo ""
    echo "👨‍💼 Staff App:"
    echo "   https://$STAFF_URL"
  else
    echo "⚠️  Staff App URL not available"
  fi
else
  echo "⚠️  Deployment '$DEPLOYMENT_NAME' not found in resource group '$RESOURCE_GROUP'"
  echo ""
  echo "Trying to retrieve URLs from Container Apps directly..."
  echo ""
  
  CUSTOMER_URL=$(az containerapp show -g "$RESOURCE_GROUP" -n "mortgages-web" --query 'properties.configuration.ingress.fqdn' -o tsv 2>/dev/null || echo "")
  STAFF_URL=$(az containerapp show -g "$RESOURCE_GROUP" -n "staff-web" --query 'properties.configuration.ingress.fqdn' -o tsv 2>/dev/null || echo "")
  
  if [ -n "$CUSTOMER_URL" ]; then
    echo "👤 Customer App:"
    echo "   https://$CUSTOMER_URL"
  fi
  
  if [ -n "$STAFF_URL" ]; then
    echo ""
    echo "👨‍💼 Staff App:"
    echo "   https://$STAFF_URL"
  fi
  
  if [ -z "$CUSTOMER_URL" ] && [ -z "$STAFF_URL" ]; then
    echo "❌ Could not find application URLs"
    echo ""
    echo "This might mean:"
    echo "  1. Apps haven't finished deploying"
    echo "  2. Apps are in paused state (run './resume-apps.sh')"
    echo "  3. Container App endpoints are not configured as public"
  fi
fi

echo ""
echo "=================="
