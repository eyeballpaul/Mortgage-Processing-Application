# 🚀 Azure Deployment Guide - Mortgage Processing Application

A complete, cost-optimized Azure deployment package for the mortgage processing application with **single VNet, multiple subnets, public apps, and private APIs/databases**.

**Budget**: £20/month | **Deployment Time**: 20-30 minutes | **Monthly Cost**: £2-5 when paused

---

## 📋 Table of Contents

1. [Quick Start](#quick-start)
2. [Architecture](#architecture)
3. [Files Overview](#files-overview)
4. [Cost Management](#cost-management)
5. [Detailed Steps](#detailed-steps)
6. [Scripts & Commands](#scripts--commands)
7. [Troubleshooting](#troubleshooting)

---

## 🎯 Quick Start

### In 5 Minutes:
```bash
# 1. Navigate to repository
cd /path/to/Mortgage-Processing-Application

# 2. Run interactive deployment
chmod +x infra/quickstart-deploy.sh
./infra/quickstart-deploy.sh

# 3. Provide:
#    - Entra App IDs & Secret
#    - Choose resource group name
#    - Select Azure region
#
# 4. Script will:
#    ✅ Deploy infrastructure
#    ✅ Build container images
#    ✅ Deploy applications
#    ✅ Provide URLs to test
```

**That's it!** Your apps are ready to test.

---

## 🏗️ Architecture

### Single VNet with Multiple Subnets

```yaml
VNet: 10.0.0.0/16 (mortgage-demo-vnet)
├── Public Subnet (10.0.1.0/24)
│   └── Optional: Application Gateway for DNS/SSL
│
├── Container Apps Subnet (10.0.2.0/24)  ← All app workloads
│   ├── 🌐 Customer Web App (PUBLIC INGRESS)
│   │   └── Accessible to: Anyone on internet
│   │   └── URL: https://mortgages-web-xxxxx.azurecontainers.io
│   │
│   ├── 🌐 Staff Web App (PUBLIC INGRESS)
│   │   └── Accessible to: Anyone (Entra auth required)
│   │   └── URL: https://staff-web-xxxxx.azurecontainers.io
│   │
│   ├── 🔒 Mortgage API (INTERNAL INGRESS)
│   │   └── Accessible to: Container Apps only
│   │   └── Communication: Private VNet
│   │
│   ├── 🔒 Products API (INTERNAL INGRESS)
│   │   └── Accessible to: Mortgage API only
│   │   └── Communication: Private VNet
│   │
│   └── 🔒 Eligibility API (INTERNAL INGRESS)
│       └── Accessible to: Mortgage API only
│       └── Communication: Private VNet
│
└── Database Subnet (10.0.3.0/24)
    ├── 🔐 SQL Server (PRIVATE ENDPOINT)
    │   ├── mortgages database
    │   ├── products database
    │   └── customer-sessions database
    │
    └── 🔐 Storage Account (PRIVATE ENDPOINT)
        └── Private blobs for documents
        └── Data Protection key stores
```

### Data Flow

```
Customer Browser
    ↓ HTTPS (PUBLIC)
Customer Web App ← Mortgage API ← Products API
    ↓                 ↓              ↓
Staff Browser   Mortgage API     Products DB
    ↓ HTTPS       (INTERNAL)
Staff Web App ← (Private VNet) ← Eligibility API
    │
    └→ SQL Server (PRIVATE)
       └→ Entra ID Auth
```

**Security Features**:
- ✅ Public web apps for customer/staff access
- ✅ Internal APIs protected from internet
- ✅ Private databases in isolated subnet
- ✅ All communication via managed identities
- ✅ No public SQL or storage endpoints
- ✅ Network isolation via VNet

---

## 📂 Files Overview

### Documentation Files (Session Workspace)
Created in `/Users/pauldavidson/.copilot/session-state/784b6336-1bfd-4ce2-aa1e-39da51cccf7e/files/`:

| File | Purpose |
|------|---------|
| `QUICK_REFERENCE.md` | ⭐ START HERE - Overview & quick commands |
| `AZURE_DEPLOYMENT_GUIDE.md` | Complete step-by-step deployment walkthrough |
| `COST_OPTIMIZATION_CHECKLIST.md` | Detailed cost breakdown & optimization strategies |

### Infrastructure Files (Repository)
Located in `infra/`:

| File | Purpose |
|------|---------|
| `main.bicep` | ⭐ Infrastructure-as-code (Bicep template) |
| `example.bicepparam` | Parameter template (copy to main.bicepparam) |
| `quickstart-deploy.sh` | 🚀 Interactive deployment wizard |
| `DEPLOYMENT_VARIABLES.md` | Parameter reference & configuration guide |

### Helper Scripts (Repository)
Located in `infra/scripts/`:

| Script | Purpose | Cost Impact |
|--------|---------|-------------|
| `check-status.sh` | View deployment status & costs | None |
| `pause-apps.sh` | ⏸️ Pause apps (save £15/month) | Reduces to £1-2/month |
| `resume-apps.sh` | ▶️ Resume apps | Increases to £18-23/month |
| `get-urls.sh` | Retrieve application URLs | None |

---

## 💰 Cost Management

### Monthly Costs Breakdown

#### Scenario A: Running 24/7
```
Container Apps (5 services):        £3-5/month
  - Customer Web App: 0.5 vCPU
  - Staff Web App: 0.5 vCPU
  - Mortgage API: 0.5 vCPU
  - Products API: 0.5 vCPU
  - Eligibility API: 0.5 vCPU

Azure SQL (3 databases, serverless):  £10-12/month
  - mortgages database
  - products database
  - customer-sessions database
  - Auto-pause enabled (but held awake by background jobs)

Storage Account (Hot tier):           £0.50/month
  - Document blobs
  - Data Protection keys

Container Registry (Basic):           £4/month
  - Image storage

Monitoring:                           £0-1/month
  - Application Insights (included)
  - Paid alerts disabled

─────────────────────────────────
TOTAL (24/7):                     £18-23/month ✅ Within budget
```

#### Scenario B: Apps Paused (Recommended) ⭐
```
Container Apps:                    £0/month
  - Scaled to 0 replicas

Azure SQL (paused):                £0.50/month
  - Storage only, compute paused
  - No background jobs active

Storage & Registry:                £1.50/month

─────────────────────────────────
TOTAL (Paused):                   £2/month ✅ Well under budget!
```

#### Scenario C: Everything Deleted
```
TOTAL (Deleted):                  £0/month ✅ No cost
Re-deploy time:                   15-20 minutes
```

### How to Minimize Costs

#### ✅ Recommended: Pause When Not Needed
```bash
# Morning: Resume
./infra/scripts/resume-apps.sh mortgage-demo-rg
# Wait 1-2 minutes for apps to start

# Evening: Pause
./infra/scripts/pause-apps.sh mortgage-demo-rg
# Cost: £2/month

# Before Interview: Resume
./infra/scripts/resume-apps.sh mortgage-demo-rg
```

**Cost**: £5-15/month (mix of running and paused)

#### ✅ Alternative: Delete When Idle
```bash
# End of week: Delete
az group delete -n mortgage-demo-rg --no-wait

# Beginning of next week: Re-deploy
./infra/quickstart-deploy.sh
```

**Cost**: £0 when deleted, £18-23 when running

#### ❌ Avoid: Running Continuously
- Continuous running costs £18-23/month
- Risk exceeding £20 budget
- Use pause scripts instead

### Budget Alert

If monthly bill exceeds £25:
1. Run `./infra/scripts/check-status.sh` to verify status
2. Run `./infra/scripts/pause-apps.sh` immediately
3. Check Azure Cost Management for unexpected resources
4. Delete resource group if still high: `az group delete -n mortgage-demo-rg`

---

## 🔧 Detailed Steps

### Step 1: Prerequisites (5 mins)
```bash
# Install Azure CLI
# https://docs.microsoft.com/cli/azure/install-azure-cli
az --version

# Install Docker Desktop
# https://www.docker.com/products/docker-desktop
docker --version

# Verify openssl
openssl version
```

### Step 2: Prepare Entra ID Configuration (10 mins)

You need two app registrations in Azure AD:

**1. Mortgage API App Registration**
```
Azure AD > App registrations > New registration
Name: Mortgage API
Supported account types: Single tenant
  
Then:
- Copy Application (client) ID
- Set Application ID URI to: api://<client-id>
- Expose an API > Add scope: Mortgage.Staff
- App roles: MortgageStaff, MortgageCustomer
- Note the values for deployment
```

**2. Staff Web App Registration**
```
Azure AD > App registrations > New registration
Name: Staff Web
Supported account types: Single tenant

Then:
- Add Web redirect URI: https://localhost:7241/signin-oidc (dev)
- Add sign-out callback: https://localhost:7241/signout-callback-oidc (dev)
- Certificates & secrets > New client secret
  - Copy the SECRET VALUE (not ID, not name)
  - You won't be able to see it again!
- API permissions > Add permission
  - Mortgage API > Delegated > Mortgage.Staff > Grant admin consent
- App roles: MortgageStaff (Users/Groups)
- Note the values for deployment
```

**3. Assign Users to Roles**
```
Mortgage API (Enterprise app):
  - Assign your user to MortgageStaff role

Staff Web (Enterprise app):
  - Assign your user to MortgageStaff role
```

### Step 3: Deploy Infrastructure (20 mins)

**Option A: Interactive (Recommended)**
```bash
cd Mortgage-Processing-Application
chmod +x infra/quickstart-deploy.sh
./infra/quickstart-deploy.sh

# Script will prompt for:
# - Resource group name
# - Azure region
# - Mortgage API Client ID
# - Staff Web Client ID
# - Staff Web Secret
# - Everything else is auto-generated
```

**Option B: Manual**
```bash
# Create parameters file
cp infra/example.bicepparam infra/main.bicepparam

# Edit with your values
nano infra/main.bicepparam

# Deploy
az group create -n mortgage-demo-rg -l uksouth

az deployment group create \
  -g mortgage-demo-rg \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

### Step 4: Build & Push Images (10 mins)

**If using `quickstart-deploy.sh`**, this is automatic.

**Manual approach**:
```bash
# Login to ACR
ACR_NAME="mortgagedemoXXXX"
az acr login --name $ACR_NAME

# Build and push images
az acr build --registry $ACR_NAME --image mortgages-web:latest \
  -f PD.Mortgages.Web/Dockerfile .

az acr build --registry $ACR_NAME --image staff-web:latest \
  -f PD.Mortgages.Staff.Web/Dockerfile .

az acr build --registry $ACR_NAME --image mortgage-api:latest \
  -f PD.Mortgages.Api/Dockerfile .

az acr build --registry $ACR_NAME --image products-api:latest \
  -f PD.Products.Api/Dockerfile .

az acr build --registry $ACR_NAME --image eligibility-api:latest \
  -f PD.Eligibility.Api/Dockerfile .
```

### Step 5: Verify Deployment (5 mins)
```bash
# Get application URLs
cd infra/scripts
./get-urls.sh mortgage-demo-rg

# Check status
./check-status.sh mortgage-demo-rg

# Test the URLs
# Open in browser:
# - Customer: https://mortgages-web-xxxxx.azurecontainers.io
# - Staff: https://staff-web-xxxxx.azurecontainers.io
```

### Step 6: Start Cost Management

**Immediate**:
```bash
# Set Azure budget alert
# Azure Portal > Subscriptions > Budgets
# Create budget: £20/month
# Alert: 90% = £18
```

**Daily**:
```bash
# End of day: Pause apps
./pause-apps.sh mortgage-demo-rg
# Cost drops to £2/month

# Morning: Resume
./resume-apps.sh mortgage-demo-rg
# Wait 1-2 minutes for apps to start
```

---

## 🎮 Scripts & Commands

### Daily Workflow

```bash
# 1. START OF DAY
cd infra/scripts
./resume-apps.sh mortgage-demo-rg          # Start apps
./get-urls.sh mortgage-demo-rg              # Get URLs
./check-status.sh mortgage-demo-rg          # Verify running

# 2. TEST APPLICATIONS
# Open URLs in browser
# Test customer app
# Test staff app (requires Entra login)

# 3. DURING DAY
# Monitor cost
./check-status.sh mortgage-demo-rg          # Check every few hours

# 4. END OF DAY
./pause-apps.sh mortgage-demo-rg            # Pause to save money
# Cost: £2/month

# 5. BEFORE INTERVIEW
./resume-apps.sh mortgage-demo-rg           # 1-2 hours before
sleep 120                                    # Wait for startup
./get-urls.sh mortgage-demo-rg              # Get URLs
./check-status.sh mortgage-demo-rg          # Verify working
```

### Maintenance Commands

```bash
# Check resource status
./check-status.sh mortgage-demo-rg

# View estimated cost
./check-status.sh mortgage-demo-rg | grep "Estimated"

# Get application URLs
./get-urls.sh mortgage-demo-rg

# Pause all services (save £15/month)
./pause-apps.sh mortgage-demo-rg

# Resume all services
./resume-apps.sh mortgage-demo-rg

# Check deployment outputs
az deployment group show -n main -g mortgage-demo-rg --query properties.outputs

# View Container App logs
az containerapp logs show -g mortgage-demo-rg -n mortgages-web --follow

# Restart an app
az containerapp revision restart \
  -g mortgage-demo-rg \
  -n mortgages-web \
  --revision <revision-name>
```

### Cleanup Commands

```bash
# Pause to save money (keep for later use)
./pause-apps.sh mortgage-demo-rg

# Delete resource group (complete cleanup, £0 cost)
az group delete -n mortgage-demo-rg --no-wait

# Wait for deletion to complete
# This takes ~20 minutes
```

---

## 🚨 Troubleshooting

### Apps Won't Start

**Symptom**: Apps show 0 replicas, won't start
```bash
./resume-apps.sh mortgage-demo-rg
./check-status.sh mortgage-demo-rg
# Wait 2-3 minutes, then check again
```

**If still not running**:
```bash
# Check container image exists
az acr repository list -n <registry-name>

# Check app logs
az containerapp logs show -g mortgage-demo-rg -n mortgages-web --follow

# Restart the app
az containerapp revision restart -g mortgage-demo-rg -n mortgages-web -r <revision-name>

# If container image is missing, rebuild and push
az acr build --registry <registry-name> --image mortgages-web:latest \
  -f PD.Mortgages.Web/Dockerfile .

# Redeploy apps
./resume-apps.sh mortgage-demo-rg
```

### Can't Get Application URLs

**Symptom**: `get-urls.sh` returns no URLs
```bash
# 1. Check if apps are running
./check-status.sh mortgage-demo-rg

# 2. If replicas = 0, resume them
./resume-apps.sh mortgage-demo-rg

# 3. Wait 1-2 minutes
sleep 120

# 4. Try again
./get-urls.sh mortgage-demo-rg
```

### Database Connection Errors

**Symptom**: Application logs show database connection failures
```bash
# Check database exists
az sql db list -g mortgage-demo-rg -s <sql-server-name>

# Check database state
az sql db show -g mortgage-demo-rg -s <sql-server-name> -n mortgages --query state

# If paused, give it time to resume (~30 seconds)
# Apps might retry and succeed

# Check managed identity permissions
az sql server ad-admin list -g mortgage-demo-rg -s <sql-server-name>

# If migrations haven't been applied
# Redeploy with deployBootstrap=true
az deployment group create \
  -g mortgage-demo-rg \
  -f infra/main.bicep \
  -p deployBootstrap=true
```

### Cost Overrun (Bill > £25)

**Immediate Action**:
```bash
# 1. Check what's running
./check-status.sh mortgage-demo-rg

# 2. Stop everything
./pause-apps.sh mortgage-demo-rg

# 3. If still high, delete
az group delete -n mortgage-demo-rg --no-wait

# 4. Investigate in Azure Portal
# Azure Portal > Subscriptions > Cost Management
# Check for unexpected resources
# Review the last billing period
```

---

## 📞 Support

### Documentation Hierarchy

1. **Quick issues?** → Troubleshooting section above
2. **Cost questions?** → `COST_OPTIMIZATION_CHECKLIST.md`
3. **Configuration help?** → `infra/DEPLOYMENT_VARIABLES.md`
4. **Full walkthrough?** → `AZURE_DEPLOYMENT_GUIDE.md`
5. **Script help?** → `infra/scripts/README.md`

### Getting Help

```bash
# Run the quickstart script (automatic setup)
./infra/quickstart-deploy.sh

# Check script output for specific error messages
# Error messages usually suggest solutions

# Check Azure Portal activity logs
# Azure Portal > Resource Group > Activity Log
# Shows deployment errors and operations

# Review Bicep validation
az deployment group validate \
  -g mortgage-demo-rg \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

---

## ✅ Pre-Deployment Checklist

Before starting, ensure you have:

- [ ] Azure subscription with sufficient quota
- [ ] Azure CLI installed and working
- [ ] Docker Desktop installed and working
- [ ] Entra ID app registrations created:
  - [ ] Mortgage API (with Mortgage.Staff scope)
  - [ ] Staff Web (with secret)
- [ ] User assigned to MortgageStaff role on both apps
- [ ] Generated secure values:
  - [ ] SQL Admin Password
  - [ ] Product Service API Key
  - [ ] Eligibility Service API Key
- [ ] Decided on:
  - [ ] Resource group name
  - [ ] Azure region (default: uksouth)
  - [ ] Cost management approach (pause/delete)

---

## 📊 Expected Timeline

| Phase | Duration | Cost |
|-------|----------|------|
| Prerequisites setup | 15 mins | £0 |
| Entra ID config | 10 mins | £0 |
| Infrastructure deployment | 15 mins | £0 (not running yet) |
| Image build & push | 15 mins | £0 |
| App deployment | 10 mins | £0 (starting) |
| Verification | 5 mins | £0.50 (running briefly) |
| **Total** | **~70 mins** | **~£0.50** |

**Monthly costs**:
- Running continuously: £18-23/month
- Paused (recommended): £2/month
- Deleted: £0/month

---

## 🎓 Learning Resources

- [Azure Container Apps](https://docs.microsoft.com/azure/container-apps/)
- [Bicep Language](https://docs.microsoft.com/azure/azure-resource-manager/bicep/)
- [Azure SQL Database Serverless](https://docs.microsoft.com/azure/azure-sql/database/serverless-tier-overview)
- [Virtual Networks](https://docs.microsoft.com/azure/virtual-network/)
- [Managed Identities](https://docs.microsoft.com/azure/active-directory/managed-identities-azure-resources/)

---

**Ready to deploy?** Run `./infra/quickstart-deploy.sh` and follow the prompts! 🚀
