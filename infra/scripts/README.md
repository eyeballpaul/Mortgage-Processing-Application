# Azure Deployment Scripts

Utility scripts for managing the mortgage processing application deployment on Azure.

## Quick Start

### 1. Deploy Infrastructure (First Time Only)

```bash
# Set environment variables
export RESOURCE_GROUP_NAME="mortgage-demo-rg"
export LOCATION="uksouth"
export CONTAINER_REGISTRY_NAME="mortgagedemo<unique>"
export SQL_SERVER_NAME="mortgage-demo-sql-<unique>"
export STORAGE_ACCOUNT_NAME="mortgagedemostg<unique>"

# Create resource group
az group create -n $RESOURCE_GROUP_NAME -l $LOCATION

# Deploy
az deployment group create \
  --resource-group $RESOURCE_GROUP_NAME \
  --template-file main.bicep \
  --parameters \
    containerRegistryName=$CONTAINER_REGISTRY_NAME \
    sqlServerName=$SQL_SERVER_NAME \
    documentStorageAccountName=$STORAGE_ACCOUNT_NAME \
    mortgageApiClientId="<your-entra-app-id>" \
    staffClientId="<your-entra-app-id>" \
    staffClientSecret="<your-secret>" \
    productServiceApiKey="$(openssl rand -hex 16)" \
    eligibilityServiceApiKey="$(openssl rand -hex 16)"
```

## Available Scripts

### check-status.sh
Check the status of all deployed resources and estimated costs.

```bash
./check-status.sh [resource-group]

# Example
./check-status.sh mortgage-demo-rg
```

**Output**: 
- Status of each Container App (running/paused)
- SQL Database states
- Storage account and container registry status
- Current estimated monthly cost

### pause-apps.sh
Pause all Container Apps to minimize costs when not in use.

```bash
./pause-apps.sh [resource-group]

# Example
./pause-apps.sh mortgage-demo-rg
```

**Cost Savings**: Reduces monthly cost from £18-23 to £1-2

**Resume**: Apps can be resumed anytime with `resume-apps.sh`

### resume-apps.sh
Resume all paused Container Apps.

```bash
./resume-apps.sh [resource-group]

# Example
./resume-apps.sh mortgage-demo-rg
```

**Wait Time**: Apps typically start within 1-2 minutes

### get-urls.sh
Retrieve the public URLs for the customer and staff web applications.

```bash
./get-urls.sh [resource-group] [deployment-name]

# Example
./get-urls.sh mortgage-demo-rg main
```

**Output**:
```
👤 Customer App: https://customer-app-xxxxx.azurecontainers.io
👨‍💼 Staff App: https://staff-app-xxxxx.azurecontainers.io
```

## Common Workflows

### Daily Development Workflow

**Morning (Start)**:
```bash
./resume-apps.sh mortgage-demo-rg
./get-urls.sh mortgage-demo-rg
```

**During Day**:
- Use the app URLs to test
- Monitor with: `./check-status.sh mortgage-demo-rg`

**Evening (End)**:
```bash
./pause-apps.sh mortgage-demo-rg
```

### Interview Preparation

```bash
# 1-2 hours before interview
./resume-apps.sh mortgage-demo-rg

# Wait 1-2 minutes for apps to start
sleep 120

# Get URLs to share
./get-urls.sh mortgage-demo-rg

# Verify everything is working
./check-status.sh mortgage-demo-rg
```

### Weekly Cleanup

```bash
# Check current status
./check-status.sh mortgage-demo-rg

# If not needed this week, delete everything
az group delete -n mortgage-demo-rg --no-wait

# To re-deploy next week, re-run the deployment from azure-pipelines.yml
# or manually run the deployment command
```

## Cost Management

### Monitor Estimated Monthly Cost
```bash
./check-status.sh mortgage-demo-rg | grep "Estimated Monthly Cost" -A 5
```

### Pause to Save Money
```bash
# Reduce from £18-23/month to £1-2/month
./pause-apps.sh mortgage-demo-rg
```

### Delete to Save Everything
```bash
# Cost: £0
# Time to re-deploy: 15-20 minutes
az group delete -n mortgage-demo-rg --no-wait
```

## Troubleshooting

### Apps Won't Start After Resume
```bash
# Check if apps are actually running
./check-status.sh mortgage-demo-rg

# Check logs
az containerapp logs show -g mortgage-demo-rg -n mortgages-web --follow

# May need to push new images and redeploy
# See: ../README.md for image building instructions
```

### Can't Find Application URLs
```bash
# 1. Check if apps are running
./check-status.sh mortgage-demo-rg

# 2. If paused, resume them
./resume-apps.sh mortgage-demo-rg

# 3. Try again
./get-urls.sh mortgage-demo-rg
```

### Database Connection Issues
```bash
# Check if SQL databases exist
az sql db list -g mortgage-demo-rg -s <sql-server-name>

# Check if database is paused (normal)
az sql db show -g mortgage-demo-rg -s <sql-server-name> -n mortgages --query state -o tsv
```

## Parameters

### Default Resource Group
All scripts default to `mortgage-demo-rg`. Override with:
```bash
./check-status.sh my-custom-resource-group
./pause-apps.sh my-custom-resource-group
./resume-apps.sh my-custom-resource-group
./get-urls.sh my-custom-resource-group
```

### Default Deployment Name
`get-urls.sh` defaults to `main`. Override with:
```bash
./get-urls.sh mortgage-demo-rg my-deployment
```

## Environment Variables

For convenience, set these once:
```bash
export AZURE_RESOURCE_GROUP="mortgage-demo-rg"
```

Then modify scripts to use:
```bash
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-mortgage-demo-rg}"
```

## Notes

- Scripts require Azure CLI to be installed: `az --version`
- Scripts require `jq` for JSON parsing (usually pre-installed)
- Scripts require appropriate Azure RBAC permissions
- Some operations may take several minutes (expect delays)
- Costs shown are estimates; check Azure Cost Management for actual costs

## Support

For issues:
1. Check script output for error messages
2. Run `./check-status.sh` to verify resource state
3. Check Azure Portal > Resource Group > Activity Log for deployment errors
4. Review main README.md for detailed deployment information
