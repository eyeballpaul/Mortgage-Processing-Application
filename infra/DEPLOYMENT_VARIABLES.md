# Azure Deployment Environment Variables Reference

> **Warning:** This older reference contains unverified commands and cost estimates.
> Use [AZURE_DEPLOYMENT.md](../AZURE_DEPLOYMENT.md) instead. In particular,
> `az account show --query id` returns a subscription ID, not a principal object ID.

This document explains all the environment variables and parameters needed for deploying the mortgage processing application to Azure.

## Network Architecture

### VNet & Subnets
```
VNet: 10.0.0.0/16 (mortgage-demo-vnet)
├── Public Subnet: 10.0.1.0/24 (public-subnet)
│   └── Application Gateway (optional for cost savings)
├── Container Apps Subnet: 10.0.2.0/24 (container-apps-subnet)
│   ├── Customer Web App (public ingress)
│   ├── Staff Web App (public ingress)
│   ├── Mortgage API (internal)
│   ├── Products API (internal)
│   └── Eligibility API (internal)
└── Database Subnet: 10.0.3.0/24 (database-subnet)
    ├── SQL Server (private endpoint)
    └── Storage Account (private endpoint)
```

## Required Parameters

### Location & Naming
```
LOCATION                          Azure region (e.g., uksouth, eastus)
RESOURCE_GROUP_NAME              Name of Azure resource group
CONTAINER_REGISTRY_NAME          ACR name (globally unique, lowercase, 5-50 chars)
SQL_SERVER_NAME                  SQL Server name (globally unique, 1-63 chars)
STORAGE_ACCOUNT_NAME             Storage account (globally unique, lowercase, 3-24 chars)
```

### Container App Names
```
MORTGAGES_WEB_APP_NAME           Default: mortgages-web
STAFF_WEB_APP_NAME               Default: staff-web
MORTGAGE_API_APP_NAME            Default: mortgage-api
PRODUCTS_API_APP_NAME            Default: products-api
ELIGIBILITY_API_APP_NAME         Default: eligibility-api
```

### Entra ID Configuration
```
MORTGAGE_API_CLIENT_ID           From Azure AD > App Registrations > Mortgage API
MORTGAGE_API_TENANT_ID           Your tenant ID
STAFF_CLIENT_ID                  From Azure AD > App Registrations > Staff Web
STAFF_CLIENT_SECRET              Client secret value (use @secure() in Bicep)
```

### Service Credentials
```
SQL_ADMIN_PASSWORD               Initial admin password (then disabled)
                                Generate: openssl rand -base64 32
                                Use @secure() in Bicep

PRODUCT_SERVICE_API_KEY          32-128 char key for Products API
                                Generate: openssl rand -hex 16
                                Use @secure() in Bicep

ELIGIBILITY_SERVICE_API_KEY      32-128 char key for Eligibility API
                                Generate: openssl rand -hex 16
                                Use @secure() in Bicep
```

### Identity & Access
```
PIPELINE_PRINCIPAL_ID            Service Principal Object ID
                                Get: az account show --query id
                                Used for CI/CD pipeline permissions
```

### Deployment Options
```
DEPLOY_APPS                      Deploy Container Apps? (true/false)
                                Set to false to deploy infrastructure only
                                Set to true after images are pushed

DEPLOY_BOOTSTRAP                 Deploy database bootstrap job? (true/false)
                                Set to true for initial database setup

IMAGE_TAG                        Container image tag (default: latest)
```

### Monitoring (Optional)
```
ALERT_EMAIL                      Email for 5xx error alerts
                                Leave empty to disable (saves cost)

OTLP_ENDPOINT                    OTLP trace collector endpoint
                                Leave empty to disable
                                Example: https://collector.example.com:4317

OTLP_PROTOCOL                    OTLP protocol: 'grpc' or 'http/protobuf'
                                Default: grpc
```

## Configuration Methods

### Method 1: Interactive Setup (Recommended for First Time)
```bash
./infra/quickstart-deploy.sh
# Script will prompt for all required values
```

### Method 2: Bicep Parameters File
```bash
# Create main.bicepparam from example.bicepparam
cp infra/example.bicepparam infra/main.bicepparam
# Edit main.bicepparam with your values

# Deploy
az deployment group create \
  --resource-group mortgage-demo-rg \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

### Method 3: Command Line Parameters
```bash
az deployment group create \
  --resource-group mortgage-demo-rg \
  --template-file infra/main.bicep \
  --parameters \
    location=uksouth \
    containerRegistryName=mortgagedemo2024 \
    sqlServerName=mortgage-demo-sql-2024 \
    # ... other parameters
```

### Method 4: Environment Variables (Advanced)
```bash
export RESOURCE_GROUP="mortgage-demo-rg"
export LOCATION="uksouth"
export CONTAINER_REGISTRY_NAME="mortgagedemo2024"
# ... set all variables

# Then use in scripts
./infra/scripts/check-status.sh $RESOURCE_GROUP
```

## Secure Value Handling

### Never Commit Secrets
❌ DO NOT:
- Commit secrets to git
- Put secrets in parameter files
- Use secrets in public logs
- Share secrets in chat/email

### Proper Secret Handling

**During Development**:
```bash
# Generate locally, never commit
STAFF_CLIENT_SECRET=$(cat /path/to/secret.txt)

# Use @secure() decorator in Bicep
# Never echo secrets to console
```

**In Azure DevOps Pipeline**:
```yaml
variables:
  - name: staffClientSecret
    value: $(staffClientSecret)  # Pipeline secret variable
  - name: productServiceApiKey
    value: $(productServiceApiKey)  # Pipeline secret variable
```

**In Key Vault** (Production):
```bash
# Store secrets in Azure Key Vault
az keyvault secret set \
  --vault-name mortgage-demo-kv \
  --name staffClientSecret \
  --value "your-secret-value"

# Reference from deployment
--parameters staffClientSecret=@Microsoft.KeyVault(SecretUri=https://...)"
```

## Generate Required Secrets

### SQL Admin Password (32+ chars, random)
```bash
openssl rand -base64 32
# Example: LQD9xK2pR7mN4vB1wFjZ8sT3hC6gE5aU9yL0mD4pK1wR2vQ5xS8tN0zM3bJ9hL

# Or using /dev/urandom
head -c 32 /dev/urandom | base64
```

### Product Service API Key (32 hex chars)
```bash
openssl rand -hex 16
# Example: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6

# Note: This is 32 characters (16 bytes * 2 for hex)
# Generate at least 32 chars, up to 128 for production
```

### Eligibility Service API Key (32 hex chars)
```bash
openssl rand -hex 16
# Example: f1e2d3c4b5a6f7e8d9c0b1a2f3e4d5c6
```

### Pipeline Principal ID
```bash
# Your current Azure account
az account show --query id --output tsv

# Or a service principal
az ad sp list --filter "displayname eq 'my-pipeline'" --query '[0].id' -o tsv
```

## Entra ID Setup

### Get Mortgage API Client ID
```bash
# From your registered app
az ad app list --filter "displayname eq 'Mortgage API'" --query '[0].appId' -o tsv

# Or in Azure Portal:
# Azure AD > App registrations > Mortgage API > Application (client) ID
```

### Get Staff Client ID & Secret
```bash
# Client ID
az ad app list --filter "displayname eq 'Staff Web'" --query '[0].appId' -o tsv

# Secret (must be created and noted when generated)
# See: Azure AD > App registrations > Staff Web > Certificates & secrets
```

### Get Tenant ID
```bash
az account show --query tenantId -o tsv
```

## Verification Checklist

Before deploying, verify you have:

- [ ] Azure subscription with sufficient quota
- [ ] Resource group name (new or existing)
- [ ] Globally unique names for:
  - Container Registry
  - SQL Server
  - Storage Account
- [ ] Entra ID app registrations with:
  - [ ] Mortgage API app registration
  - [ ] Staff Web app registration
  - [ ] Staff Web client secret
  - [ ] Proper role assignments
- [ ] Generated secure values:
  - [ ] SQL Admin Password
  - [ ] Product Service API Key
  - [ ] Eligibility Service API Key
- [ ] Service Principal Object ID for pipeline
- [ ] Email (optional) for alerts
- [ ] OTLP endpoint (optional) for traces

## Common Issues

### "Name already exists" Error
**Problem**: Global resource name is taken
**Solution**: Append random suffix to names
```bash
UNIQUE=$(date +%s)
CONTAINER_REGISTRY_NAME="mortgagedemo${UNIQUE: -4}"
```

### "Insufficient permissions" Error
**Problem**: Service principal lacks required permissions
**Solution**: Assign required roles
```bash
# Contributor role
az role assignment create \
  --assignee <principal-id> \
  --role Contributor \
  --scope /subscriptions/<subscription-id>

# SQL permissions for Entra setup
# Azure AD > Roles and administrators > Directory Roles > 
#   "SQL Database Administrator" or higher
```

### "Secret value too short" Error
**Problem**: API key is not 32+ characters
**Solution**: Use proper length secret
```bash
openssl rand -hex 16  # Produces 32 chars
openssl rand -hex 32  # Produces 64 chars (even better)
```

### "Client secret is invalid" Error
**Problem**: Pasting secret wrong value or expired
**Solution**: Regenerate or use correct value
```bash
# Generate new secret in Azure Portal
# Azure AD > App registrations > Staff Web > 
#   Certificates & secrets > New client secret
# Copy immediately (you won't see it again)
```

## Cost Optimization Tips

### Keep Costs Low
1. Use `uksouth` or lowest-cost region
2. Keep Container Apps replicas at 0 when idle
3. Enable SQL auto-pause (60 minute delay)
4. Don't enable optional alerts/tracing
5. Delete resource group if not used for >1 week

### Monitor Costs
```bash
# In Azure Portal
# Resource Group > Cost Analysis

# Or via CLI
az consumption budget list \
  --resource-group mortgage-demo-rg
```

### Save Money
```bash
# Pause apps (reduces to £1-2/month)
./infra/scripts/pause-apps.sh mortgage-demo-rg

# Delete everything (reduces to £0)
az group delete --name mortgage-demo-rg --no-wait
```

## References

- [Azure CLI Documentation](https://docs.microsoft.com/cli/azure/)
- [Bicep Language Reference](https://docs.microsoft.com/azure/azure-resource-manager/bicep/)
- [Container Apps Documentation](https://docs.microsoft.com/azure/container-apps/)
- [Azure SQL Database Pricing](https://azure.microsoft.com/pricing/details/sql-database/)
- [Entra ID Application Management](https://docs.microsoft.com/azure/active-directory/manage-apps/)
