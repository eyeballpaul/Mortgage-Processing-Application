#!/bin/bash
# Check deployment status and estimated costs
# Usage: ./check-status.sh [resource-group]
# Default resource group: mortgage-demo-rg

set -e

RESOURCE_GROUP="${1:-mortgage-demo-rg}"

echo "📊 Deployment Status Report"
echo "=========================="
echo ""

# Check if resource group exists
if ! az group exists --name "$RESOURCE_GROUP" -o tsv | grep -q "true"; then
  echo "❌ Resource group '$RESOURCE_GROUP' does not exist"
  exit 1
fi

echo "Resource Group: $RESOURCE_GROUP"
echo ""

# Container Apps Status
echo "🐳 Container Apps Status:"
APPS=("mortgages-web" "staff-web" "mortgage-api" "products-api" "eligibility-api")
TOTAL_REPLICAS=0

for app in "${APPS[@]}"; do
  if az containerapp exists --resource-group "$RESOURCE_GROUP" --name "$app" -o tsv | grep -q "true"; then
    PROVISIONING_STATE=$(az containerapp show -g "$RESOURCE_GROUP" -n "$app" --query 'properties.provisioningState' -o tsv)
    REPLICAS=$(az containerapp show -g "$RESOURCE_GROUP" -n "$app" --query 'properties.template.scale.minReplicas' -o tsv)
    TOTAL_REPLICAS=$((TOTAL_REPLICAS + REPLICAS))
    STATUS_ICON="✅"
    if [ "$REPLICAS" = "0" ]; then
      STATUS_ICON="⏸️ "
    fi
    echo "  $STATUS_ICON $app: $REPLICAS replica(s) [$PROVISIONING_STATE]"
  else
    echo "  ❌ $app: Not found"
  fi
done

echo ""

# SQL Databases Status
echo "🗄️  SQL Database Status:"
if az sql server list -g "$RESOURCE_GROUP" --query '[0].name' -o tsv 2>/dev/null | grep -q .; then
  SQL_SERVER=$(az sql server list -g "$RESOURCE_GROUP" --query '[0].name' -o tsv)
  echo "  SQL Server: $SQL_SERVER"
  
  for db in mortgages products customer-sessions; do
    if az sql db exists --resource-group "$RESOURCE_GROUP" --server "$SQL_SERVER" --name "$db" -o tsv 2>/dev/null | grep -q "true"; then
      DB_STATE=$(az sql db show -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "$db" --query 'state' -o tsv)
      DB_EDITION=$(az sql db show -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "$db" --query 'edition' -o tsv)
      DB_STATUS="✅"
      if [ "$DB_STATE" = "Paused" ]; then
        DB_STATUS="⏸️ "
      fi
      echo "    $DB_STATUS $db [$DB_STATE, $DB_EDITION]"
    fi
  done
else
  echo "  ⚠️  No SQL Server found"
fi

echo ""

# Storage Account Status
echo "📦 Storage Account Status:"
STORAGE=$(az storage account list -g "$RESOURCE_GROUP" --query '[0].name' -o tsv)
if [ -n "$STORAGE" ]; then
  TIER=$(az storage account show -g "$RESOURCE_GROUP" -n "$STORAGE" --query 'sku.tier' -o tsv)
  echo "  ✅ $STORAGE [$TIER]"
else
  echo "  ⚠️  No storage account found"
fi

echo ""

# Container Registry Status
echo "📮 Container Registry Status:"
ACR=$(az acr list -g "$RESOURCE_GROUP" --query '[0].name' -o tsv)
if [ -n "$ACR" ]; then
  ACR_SKU=$(az acr show -g "$RESOURCE_GROUP" -n "$ACR" --query 'sku.name' -o tsv)
  echo "  ✅ $ACR [$ACR_SKU]"
else
  echo "  ⚠️  No container registry found"
fi

echo ""

echo ""
echo "=========================="
