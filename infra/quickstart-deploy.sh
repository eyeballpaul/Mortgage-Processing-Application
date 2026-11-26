#!/usr/bin/env bash
# Quick Start Deployment Script for Azure
# This script will guide you through the entire deployment process

set -e

echo "================================"
echo "Mortgage Processing App - Azure"
echo "Quick Start Deployment"
echo "================================"
echo ""

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_step() {
  echo -e "${GREEN}[STEP]${NC} $1"
}

print_error() {
  echo -e "${RED}[ERROR]${NC} $1"
}

print_warning() {
  echo -e "${YELLOW}[WARNING]${NC} $1"
}

# Step 1: Check prerequisites
print_step "Checking prerequisites..."

if ! command -v az &> /dev/null; then
  print_error "Azure CLI is not installed. Please install it from: https://docs.microsoft.com/cli/azure/install-azure-cli"
  exit 1
fi

if ! command -v docker &> /dev/null; then
  print_error "Docker is not installed. Please install it from: https://www.docker.com/products/docker-desktop"
  exit 1
fi

if ! command -v openssl &> /dev/null; then
  print_error "OpenSSL is not installed. Please install it."
  exit 1
fi

echo "✅ All prerequisites found"
echo ""

# Step 2: Azure Login
print_step "Checking Azure login..."

if ! az account show &> /dev/null; then
  echo "Opening Azure login page..."
  az login --use-device-code
fi

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
SUBSCRIPTION_NAME=$(az account show --query name -o tsv)
echo "✅ Logged in to: $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)"
echo ""

# Step 3: Gather configuration
print_step "Gathering configuration..."
echo ""

# Resource Group
read -p "Resource Group Name (default: mortgage-demo-rg): " RESOURCE_GROUP
RESOURCE_GROUP="${RESOURCE_GROUP:-mortgage-demo-rg}"

# Location
read -p "Azure Region (default: uksouth): " LOCATION
LOCATION="${LOCATION:-uksouth}"

# Generate unique suffix
UNIQUE_SUFFIX=$(date +%s%N | tail -c 6)

# Container Registry
read -p "Container Registry Name (default: mortgagedemo$UNIQUE_SUFFIX): " CONTAINER_REGISTRY_NAME
CONTAINER_REGISTRY_NAME="${CONTAINER_REGISTRY_NAME:-mortgagedemo$UNIQUE_SUFFIX}"

# SQL Server
read -p "SQL Server Name (default: mortgage-demo-sql-$UNIQUE_SUFFIX): " SQL_SERVER_NAME
SQL_SERVER_NAME="${SQL_SERVER_NAME:-mortgage-demo-sql-$UNIQUE_SUFFIX}"

# Storage Account
read -p "Storage Account Name (default: mortgagedemostg$UNIQUE_SUFFIX): " STORAGE_ACCOUNT_NAME
STORAGE_ACCOUNT_NAME="${STORAGE_ACCOUNT_NAME:-mortgagedemostg$UNIQUE_SUFFIX}"

# Entra Configuration
read -p "Mortgage API Client ID: " MORTGAGE_API_CLIENT_ID
read -p "Staff Web Client ID: " STAFF_CLIENT_ID
read -sp "Staff Web Client Secret: " STAFF_CLIENT_SECRET
echo ""

echo ""
print_step "Configuration Summary:"
echo "  Resource Group: $RESOURCE_GROUP"
echo "  Location: $LOCATION"
echo "  Container Registry: $CONTAINER_REGISTRY_NAME"
echo "  SQL Server: $SQL_SERVER_NAME"
echo "  Storage Account: $STORAGE_ACCOUNT_NAME"
echo "  Mortgage API Client ID: $MORTGAGE_API_CLIENT_ID"
echo "  Staff Client ID: $STAFF_CLIENT_ID"
echo ""

read -p "Continue with this configuration? (y/n): " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
  print_error "Deployment cancelled"
  exit 1
fi
echo ""

# Step 4: Create Resource Group
print_step "Creating Azure Resource Group..."

if az group exists --name "$RESOURCE_GROUP" -o tsv | grep -q "true"; then
  echo "✅ Resource group '$RESOURCE_GROUP' already exists"
else
  az group create \
    --name "$RESOURCE_GROUP" \
    --location "$LOCATION" > /dev/null
  echo "✅ Created resource group: $RESOURCE_GROUP"
fi
echo ""

# Step 5: Generate secure values
print_step "Generating secure configuration values..."

SQL_ADMIN_PASSWORD=$(openssl rand -base64 32)
PRODUCT_SERVICE_API_KEY=$(openssl rand -hex 16)
ELIGIBILITY_SERVICE_API_KEY=$(openssl rand -hex 16)
PIPELINE_PRINCIPAL_ID=$(az account show --query id -o tsv)

echo "✅ Generated secure values"
echo "⚠️  Save these in a secure location:"
echo "   SQL Admin Password: (saved securely)"
echo "   Product Service API Key: (saved securely)"
echo "   Eligibility Service API Key: (saved securely)"
echo ""

# Step 6: Deploy Infrastructure
print_step "Deploying infrastructure with Bicep..."
echo "(This may take 5-10 minutes...)"
echo ""

az deployment group create \
  --name main \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/main.bicep \
  --parameters \
    location="$LOCATION" \
    containerRegistryName="$CONTAINER_REGISTRY_NAME" \
    containerAppEnvironmentName="mortgage-demo-env" \
    sqlServerName="$SQL_SERVER_NAME" \
    documentStorageAccountName="$STORAGE_ACCOUNT_NAME" \
    mortgagesWebAppName="mortgages-web" \
    staffWebAppName="staff-web" \
    mortgageApiAppName="mortgage-api" \
    productsApiAppName="products-api" \
    eligibilityApiAppName="eligibility-api" \
    mortgageApiClientId="$MORTGAGE_API_CLIENT_ID" \
    staffClientId="$STAFF_CLIENT_ID" \
    staffClientSecret="$STAFF_CLIENT_SECRET" \
    pipelinePrincipalId="$PIPELINE_PRINCIPAL_ID" \
    sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
    productServiceApiKey="$PRODUCT_SERVICE_API_KEY" \
    eligibilityServiceApiKey="$ELIGIBILITY_SERVICE_API_KEY" \
    deployApps=false \
    deployBootstrap=false

echo "✅ Infrastructure deployed successfully"
echo ""

# Step 7: Build and push images
print_step "Building and pushing Container images..."
echo ""

az acr login --name "$CONTAINER_REGISTRY_NAME"

echo "Building images... (this may take 5-10 minutes)"
az acr build --registry "$CONTAINER_REGISTRY_NAME" --image mortgages-web:latest -f PD.Mortgages.Web/Dockerfile . > /dev/null
az acr build --registry "$CONTAINER_REGISTRY_NAME" --image staff-web:latest -f PD.Mortgages.Staff.Web/Dockerfile . > /dev/null
az acr build --registry "$CONTAINER_REGISTRY_NAME" --image mortgage-api:latest -f PD.Mortgages.Api/Dockerfile . > /dev/null
az acr build --registry "$CONTAINER_REGISTRY_NAME" --image products-api:latest -f PD.Products.Api/Dockerfile . > /dev/null
az acr build --registry "$CONTAINER_REGISTRY_NAME" --image eligibility-api:latest -f PD.Eligibility.Api/Dockerfile . > /dev/null

echo "✅ All images built and pushed"
echo ""

# Step 8: Deploy applications
print_step "Deploying Container Apps..."
echo "(This may take 5-10 minutes...)"
echo ""

az deployment group create \
  --name main \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/main.bicep \
  --parameters \
    location="$LOCATION" \
    containerRegistryName="$CONTAINER_REGISTRY_NAME" \
    containerAppEnvironmentName="mortgage-demo-env" \
    sqlServerName="$SQL_SERVER_NAME" \
    documentStorageAccountName="$STORAGE_ACCOUNT_NAME" \
    mortgagesWebAppName="mortgages-web" \
    staffWebAppName="staff-web" \
    mortgageApiAppName="mortgage-api" \
    productsApiAppName="products-api" \
    eligibilityApiAppName="eligibility-api" \
    mortgageApiClientId="$MORTGAGE_API_CLIENT_ID" \
    staffClientId="$STAFF_CLIENT_ID" \
    staffClientSecret="$STAFF_CLIENT_SECRET" \
    pipelinePrincipalId="$PIPELINE_PRINCIPAL_ID" \
    sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
    productServiceApiKey="$PRODUCT_SERVICE_API_KEY" \
    eligibilityServiceApiKey="$ELIGIBILITY_SERVICE_API_KEY" \
    deployApps=true \
    deployBootstrap=true

echo "✅ Container Apps deployed"
echo ""

# Step 9: Get application URLs
print_step "Retrieving application URLs..."

CUSTOMER_URL=$(az deployment group show -n main -g "$RESOURCE_GROUP" --query 'properties.outputs.mortgagesWebUrl.value' -o tsv)
STAFF_URL=$(az deployment group show -n main -g "$RESOURCE_GROUP" --query 'properties.outputs.staffWebUrl.value' -o tsv)

echo "✅ Applications are ready!"
echo ""
echo "================================"
echo "Deployment Complete!"
echo "================================"
echo ""
echo "👤 Customer App:"
echo "   https://$CUSTOMER_URL"
echo ""
echo "👨‍💼 Staff App:"
echo "   https://$STAFF_URL"
echo ""
echo "================================"
echo "Cost Management"
echo "================================"
echo ""
echo "Current estimated monthly cost: £18-23 (running 24/7)"
echo ""
echo "To reduce costs when not in use:"
echo "   cd infra/scripts"
echo "   ./pause-apps.sh $RESOURCE_GROUP"
echo ""
echo "This reduces cost to £1-2/month"
echo ""
echo "To resume:"
echo "   ./resume-apps.sh $RESOURCE_GROUP"
echo ""
echo "To delete everything and stop all costs:"
echo "   az group delete -n $RESOURCE_GROUP --no-wait"
echo ""
echo "================================"
echo "Next Steps"
echo "================================"
echo ""
echo "1. Test the applications"
echo "2. Monitor costs in Azure Portal"
echo "3. Run pause-apps.sh when done for the day"
echo "4. Run resume-apps.sh when needed again"
echo ""
echo "For more information, see:"
echo "  - infra/scripts/README.md"
echo "  - AZURE_DEPLOYMENT_GUIDE.md"
echo "  - COST_OPTIMIZATION_CHECKLIST.md"
echo ""
