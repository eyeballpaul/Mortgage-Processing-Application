// Example Bicep parameters file for cost-optimized deployment
// Copy this to main.bicepparam and customize for your environment

using './main.bicep'

// Azure Region (uksouth is good for UK-based testing)
param location = 'uksouth'

// Globally unique names (append random suffix to avoid conflicts)
// Example: mortgagedemo2024
param containerRegistryName = 'mortgagedemoxxx'

// Container Apps Environment name (can include hyphens)
param containerAppEnvironmentName = 'mortgage-demo-env'

// SQL Server name (must be globally unique)
param sqlServerName = 'mortgage-demo-sql-xxx'

// Storage account name (must be globally unique, lowercase, 3-24 chars)
param documentStorageAccountName = 'mortgagedemostgxxx'

// Container App names
param mortgagesWebAppName = 'mortgages-web'
param staffWebAppName = 'staff-web'
param mortgageApiAppName = 'mortgage-api'
param productsApiAppName = 'products-api'
param eligibilityApiAppName = 'eligibility-api'

// Entra ID Configuration
// Get these from your app registrations in Azure AD
param mortgageApiClientId = '<your-mortgage-api-client-id>'
param staffClientId = '<your-staff-web-client-id>'
@secure()
param staffClientSecret = '<your-staff-web-secret-value>'

// Container image tag (use 'latest' or specific version)
param imageTag = 'latest'

// Azure DevOps Service Connection Principal
// Get with: az account show --query id
param pipelinePrincipalId = '<your-pipeline-principal-object-id>'

// SQL Admin Password (bootstrap only, then disabled)
// Generate with: openssl rand -base64 32
@secure()
param sqlAdminPassword = '<generate-strong-password>'

// API Keys for internal services
// Generate with: openssl rand -hex 16 (produces 32 char hex string)
@secure()
param productServiceApiKey = '<your-32-char-random-key>'
@secure()
param eligibilityServiceApiKey = '<your-32-char-random-key>'

// Deployment Flags
param deployApps = true          // Deploy Container Apps after images are pushed
param deployBootstrap = false    // Deploy database bootstrap job

// Optional Monitoring (leave empty to disable and save cost)
param alertEmail = ''            // Set to email to enable 5xx alerts (adds cost)
param otlpEndpoint = ''          // Set to OTLP endpoint to enable trace export
param otlpProtocol = 'grpc'      // 'grpc' or 'http/protobuf'
