// Bicep describes the desired Azure resources; Azure creates or updates them when
// the deployment scripts submit this file. It is not a sequence of shell commands.
// The scripts deploy the foundation, bootstrap job and apps in separate phases.
// See ../docs/azure-advanced-deployment.md before deploying: resources cost money,
// the worker has not yet been validated in Azure, and a budget does not cap costs.
// All resources below belong to the resource group selected by the deployment.
// Reading this file: param is an input, var is a reusable value, resource is an
// Azure object, module calls another Bicep file, and output returns a value.
// @description and other @ decorators document or validate inputs. A resource's
// quoted type includes its Azure API version. Its properties configure the
// object; parent and scope locate it, and dependsOn controls creation ordering.
// An "if" condition skips a declaration when false; ! asserts a conditional
// resource exists where the condition makes it safe to read.
targetScope = 'resourceGroup'

// Where resources are created; use the resource group's region unless overridden.
@description('Azure region for the deployment.')
param location string = resourceGroup().location

// The registry stores the container images; Azure requires this name to be globally
// unique. The decorators enforce the permitted length, not global uniqueness.
@description('Globally unique, lowercase Azure Container Registry name.')
@minLength(5)
@maxLength(50)
param containerRegistryName string

// Names the shared Container Apps environment hosting the services and job.
@description('Container Apps managed environment name.')
param containerAppEnvironmentName string

// Names the logical SQL server that contains all four separate databases.
@description('Globally unique Azure SQL logical server name.')
@minLength(1)
@maxLength(63)
param sqlServerName string

// Names the private storage account for documents and web-app protection keys.
@description('Globally unique, lowercase storage account name for private mortgage document blobs.')
@minLength(3)
@maxLength(24)
param documentStorageAccountName string

// Name used for the publicly reachable customer web app in Container Apps.
@description('Name of the customer mortgage web Container App.')
param mortgagesWebAppName string

// Name used for the internal product-catalog API.
@description('Name of the Products API Container App.')
param productsApiAppName string

// Name used for the internal eligibility API.
@description('Name of the internal Eligibility API Container App.')
param eligibilityApiAppName string

// Name used for the internal Mortgage API, which owns application data.
@description('Name of the internal Mortgage API Container App.')
param mortgageApiAppName string

// Name used for the publicly reachable staff web app.
@description('Name of the staff web Container App.')
param staffWebAppName string

// Identifies the existing Entra API registration, so apps can request API tokens.
@description('Application/client ID of the Mortgage API Entra app registration.')
param mortgageApiClientId string

// Identifies the existing Entra staff registration used for staff sign-in.
@description('Application/client ID of the staff web Entra app registration.')
param staffClientId string

// Staff sign-in uses this secret value. Supply it securely, never in source control;
// @secure prevents Azure from displaying it as an ordinary deployment parameter.
@description('Client secret value for the staff web Entra app registration.')
@secure()
param staffClientSecret string

// Selects which built images Container Apps runs; quickstart defaults to latest.
@description('Image tag produced by the pipeline.')
param imageTag string = 'latest'

// The directory object ID, not application client ID, allowed to push images.
@description('Object ID of the user or service principal granted AcrPush.')
param pipelinePrincipalId string

// Tells Azure whether the push principal is a person or application identity.
@allowed(['User', 'ServicePrincipal'])
@description('Directory principal type of the identity granted AcrPush.')
param pipelinePrincipalType string = 'ServicePrincipal'

// Include creation-only SQL administrator settings for a new server. The
// quickstart sets false for an existing server to avoid reapplying those settings.
@description('Set to true only when creating a new SQL server; false when redeploying an existing server.')
param createSqlServer bool = true

// Shared secret for Mortgage-to-Products calls; both apps must receive the same
// value. Supply it securely, not in a committed parameters file.
@description('Random API key shared by the internal Products API and its callers. Supply securely.')
@secure()
param productServiceApiKey string

// Separate shared secret for Mortgage-to-Eligibility calls; keep it distinct
// from the Products key and supply it securely.
@description('Random API key shared by the internal Eligibility API and Mortgage API.')
@secure()
param eligibilityServiceApiKey string

// The scripts set this after images and database preparation are ready; false
// allows the foundation to be deployed without starting application containers.
@description('Deploy the application Container Apps after their images have been pushed.')
param deployApps bool = true

// Enables the manually started SQL preparation job in its own deployment phase.
@description('Deploy the manual database bootstrap job after its image has been pushed.')
param deployBootstrap bool = false

// A nonempty address enables a paid alert after apps are deployed. Clearing it
// in an incremental deployment does not delete an alert that already exists.
@description('Optional email for the paid Azure Monitor 5xx log alert; leave empty to disable it.')
param alertEmail string = ''

// Optional external trace collector. An empty value leaves export disabled;
// an operator supplies and trusts the collector separately.
@description('Optional trusted OTLP collector endpoint for sampled traces; leave empty to disable export. Do not include credentials.')
param otlpEndpoint string = ''

// Match the trace transport to the collector; Azure accepts only these choices.
@allowed(['grpc', 'http/protobuf'])
@description('OTLP transport protocol used when trace export is enabled.')
param otlpProtocol string = 'grpc'

// Look up the built-in AcrPush role ID in this subscription so the deployment
// principal can upload images.
var acrPushRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
// Look up the built-in AcrPull role ID so Container Apps can download images.
var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
// Common labels identify resources managed by this prototype, including during
// the cautious deletion workflow; they are not an authorization boundary.
var appTags = {
  application: 'pd-mortgages-demo'
  managedBy: 'bicep'
}

// Create the single private network in the selected group. Its /16 range is
// divided into the two non-overlapping subnets below.
resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${containerAppEnvironmentName}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: ['10.42.0.0/16']
    }
  }
  tags: appTags
}

// Reserve and delegate this subnet to Container Apps, so Azure can place its
// environment's infrastructure in the VNet.
resource appSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: virtualNetwork
  name: 'container-apps'
  properties: {
    addressPrefix: '10.42.0.0/24'
    delegations: [
      {
        name: 'container-apps'
        properties: {
          serviceName: 'Microsoft.App/environments'
        }
      }
    ]
  }
}

// Reserve a second subnet for SQL and Blob private endpoints. Wait for the app
// subnet first because Azure can reject concurrent VNet subnet updates.
resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: virtualNetwork
  name: 'private-endpoints'
  properties: {
    addressPrefix: '10.42.1.0/24'
    privateEndpointNetworkPolicies: 'Disabled'
  }
  dependsOn: [
    appSubnet
  ]
}

// The private SQL DNS zone holds records pointing the normal SQL hostname at
// its private endpoint. Azure Private DNS zones use the global location.
resource sqlPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.database.windows.net'
  location: 'global'
  tags: appTags
}

// The equivalent private DNS zone resolves Blob storage to its private endpoint.
resource blobPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.blob.core.windows.net'
  location: 'global'
  tags: appTags
}

// Link the SQL zone to this VNet so workloads can resolve the private address;
// automatic registration of unrelated VMs is disabled.
resource sqlPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlPrivateDns
  name: 'demo-vnet'
  location: 'global'
  properties: {
    virtualNetwork: { id: virtualNetwork.id }
    registrationEnabled: false
  }
}

// Link the Blob zone to the same VNet for private storage name resolution.
resource blobPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: blobPrivateDns
  name: 'demo-vnet'
  location: 'global'
  properties: {
    virtualNetwork: { id: virtualNetwork.id }
    registrationEnabled: false
  }
}

// Store container images in a Basic registry. Its endpoint is public, but admin
// credentials are disabled; pushes and pulls use the scoped identities below.
resource registry 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: containerRegistryName
  location: location
  sku: {
	name: 'Basic'
  }
  properties: {
	adminUserEnabled: false
	publicNetworkAccess: 'Enabled'
  }
  tags: appTags
}

// Grant the selected deployment principal push access at this registry's scope.
// A deterministic GUID keeps redeployments from creating duplicate assignments;
// other existing assignments can also grant access.
resource pipelinePushRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pipelinePrincipalId, acrPushRoleDefinitionId)
  scope: registry
  properties: {
	roleDefinitionId: acrPushRoleDefinitionId
	principalId: pipelinePrincipalId
	principalType: pipelinePrincipalType
  }
}

// This retained identity lets the environment pull private registry images
// without a registry password; the role assignment is defined below.
resource imagePullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-pull'
  location: location
  tags: appTags
}

// The Mortgage API uses this separate identity for SQL, documents and sending
// events. Its permissions are granted at the relevant resource scopes.
resource mortgagesIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-mortgages'
  location: location
  tags: appTags
}

// The notification worker uses this identity to receive events and access its SQL database.
resource notificationsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-notifications'
  location: location
  tags: appTags
}

// A distinct scaler identity reads Service Bus metrics. It receives a powerful
// namespace-scoped role and is configured below as unavailable to containers.
resource notificationsScalerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-notifications-scaler'
  location: location
  tags: appTags
}

// Deploy the companion template in this group to create the public Service Bus
// namespace, queue and scoped roles using the three identities defined here.
module notifications './notifications.bicep' = {
  name: 'mortgage-notifications'
  params: {
    location: location
    mortgagesIdentityName: mortgagesIdentity.name
    notificationsIdentityName: notificationsIdentity.name
    notificationsScalerIdentityName: notificationsScalerIdentity.name
  }
}

// Products API uses this identity for its own SQL database.
resource productsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-products'
  location: location
  tags: appTags
}

// Customer web uses this identity for session SQL, protection keys and API tokens.
resource customerSessionsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-customer-sessions'
  location: location
  tags: appTags
}

// The short-lived bootstrap job uses this SQL administrator identity to set up
// databases and runtime users; normal app identities do not run migrations.
resource bootstrapIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-bootstrap'
  location: location
  tags: appTags
}

// Staff web uses this identity for its own Data Protection key container.
resource staffIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-staff'
  location: location
  tags: appTags
}

// Grant the pull identity AcrPull on this registry, rather than at group scope.
resource imagePullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, imagePullIdentity.id, acrPullRoleDefinitionId)
  scope: registry
  properties: {
	roleDefinitionId: acrPullRoleDefinitionId
	principalId: imagePullIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

// Create the Blob account for documents and two independent web-app key stores.
// Public access and shared keys are disabled; private networking, HTTPS, TLS
// and managed identities protect access. Standard LRS is locally redundant.
resource documentStorage 'Microsoft.Storage/storageAccounts@2026-09-01' = {
  name: documentStorageAccountName
  location: location
  sku: {
	name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
	accessTier: 'Hot'
	allowBlobPublicAccess: false
	allowSharedKeyAccess: false
	minimumTlsVersion: 'TLS1_2'
	publicNetworkAccess: 'Disabled'
	supportsHttpsTrafficOnly: true
  }
  tags: appTags
}

// Store mortgage documents in their own non-public container.
resource documentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${documentStorage.name}/default/mortgage-documents'
  properties: {
	publicAccess: 'None'
  }
}

// Store customer web's Data Protection keys separately so sessions and cookies
// can be decrypted across replicas and restarts.
resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${documentStorage.name}/default/data-protection'
  properties: {
	publicAccess: 'None'
  }
}

// Keep staff web's Data Protection keys in a different non-public container.
resource staffDataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${documentStorage.name}/default/staff-data-protection'
  properties: {
    publicAccess: 'None'
  }
}

// Grant the Mortgage API identity Blob read/write access only to documents.
// The role assignment name is deterministic so redeployment is repeatable.
resource documentStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(documentContainer.id, mortgagesIdentity.id, 'Storage Blob Data Contributor')
  scope: documentContainer
  properties: {
	roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
	principalId: mortgagesIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

// Grant customer web Blob read/write access only to its own key container.
resource dataProtectionStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionContainer.id, customerSessionsIdentity.id, 'Storage Blob Data Contributor')
  scope: dataProtectionContainer
  properties: {
	roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
	principalId: customerSessionsIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

// Grant staff web Blob read/write access only to its own key container.
resource staffDataProtectionStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(staffDataProtectionContainer.id, staffIdentity.id, 'Storage Blob Data Contributor')
  scope: staffDataProtectionContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: staffIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Create or update the logical SQL server with public access disabled and TLS
// 1.2 minimum. On first creation, union adds the bootstrap Entra administrator;
// on later deployments it omits creation-only settings.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  properties: union({
    publicNetworkAccess: 'Disabled'
    minimalTlsVersion: '1.2'
  }, createSqlServer ? {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: bootstrapIdentity.name
      sid: bootstrapIdentity.properties.principalId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  } : {})
  tags: appTags
}

// Configure the bootstrap identity as the server's Entra administrator, so
// the private bootstrap job can create database users and apply migrations.
resource sqlEntraAdmin 'Microsoft.Sql/servers/administrators@2022-05-01-preview' = {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
	administratorType: 'ActiveDirectory'
	login: bootstrapIdentity.name
	sid: bootstrapIdentity.properties.principalId
	tenantId: tenant().tenantId
  }
}

// Require Entra authentication instead of SQL passwords. Wait until the Entra
// administrator above exists before enabling this server setting.
resource sqlEntraOnly 'Microsoft.Sql/servers/azureADOnlyAuthentications@2022-05-01-preview' = {
  parent: sqlServer
  name: 'Default'
  properties: {
	azureADOnlyAuthentication: true
  }
  dependsOn: [
	sqlEntraAdmin
  ]
}

// Give the SQL server a private address in the endpoint subnet; applications
// connect through this endpoint rather than through the public internet.
resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${sqlServerName}-private'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnet.id }
    privateLinkServiceConnections: [
      {
        name: 'sql'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
  tags: appTags
}

// Connect the SQL endpoint to the SQL private DNS zone so Azure manages the
// endpoint's private DNS record.
resource sqlPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql'
        properties: { privateDnsZoneId: sqlPrivateDns.id }
      }
    ]
  }
}

// Give the Blob service a private address in the same endpoint subnet. The
// explicit dependency serializes endpoint creation to avoid network conflicts.
resource blobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${documentStorageAccountName}-private'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnet.id }
    privateLinkServiceConnections: [
      {
        name: 'blob'
        properties: {
          privateLinkServiceId: documentStorage.id
          groupIds: ['blob']
        }
      }
    ]
  }
  tags: appTags
  dependsOn: [
    sqlPrivateEndpoint
  ]
}

// Connect the Blob endpoint to its private DNS zone for name resolution.
resource blobPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: blobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob'
        properties: { privateDnsZoneId: blobPrivateDns.id }
      }
    ]
  }
}

// Store applications, audit history and outbox records in the Mortgage database.
// Each database below requests General Purpose serverless, 0.5-2 vCores, 32 GiB,
// local backup redundancy and the free offer. AutoPause here applies when the
// free allowance is exhausted: the database is unavailable until it resets.
// Free-offer eligibility and actual charges depend on the subscription.
resource mortgagesDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'mortgages'
  location: location
  sku: {
	name: 'GP_S_Gen5'
	tier: 'GeneralPurpose'
	family: 'Gen5'
	capacity: 2
  }
  properties: {
	useFreeLimit: true
	freeLimitExhaustionBehavior: 'AutoPause'
	maxSizeBytes: 34359738368
	requestedBackupStorageRedundancy: 'Local'
	minCapacity: json('0.5')
  }
  tags: appTags
}

// Keep the product catalog in its own database, with the same serverless and
// free-offer settings as mortgages. Its migration history is independent.
resource productsDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'products'
  location: location
  sku: {
	name: 'GP_S_Gen5'
	tier: 'GeneralPurpose'
	family: 'Gen5'
	capacity: 2
  }
  properties: {
	useFreeLimit: true
	freeLimitExhaustionBehavior: 'AutoPause'
	maxSizeBytes: 34359738368
	requestedBackupStorageRedundancy: 'Local'
	minCapacity: json('0.5')
  }
  tags: appTags
}

// Keep customer web session data in a separate database with the same settings;
// this prevents the web app from needing direct access to mortgage data.
resource customerSessionsDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'customer-sessions'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    minCapacity: json('0.5')
  }
  tags: appTags
}

// Keep the worker's simulated notification and deduplication records in a fourth
// database with the same settings. The worker does not send actual email.
resource notificationsDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'notifications'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    minCapacity: json('0.5')
  }
  tags: appTags
}

// Collect Container Apps logs here for troubleshooting. Retention is 30 days
// and the per-GB plan can incur ingestion charges.
resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2026-03-01' = {
  name: '${containerAppEnvironmentName}-logs'
  location: location
  properties: {
	retentionInDays: 30
	sku: {
	  name: 'PerGB2018'
	}
  }
  tags: appTags
}

// Only when apps are deployed and an email is supplied, define where Azure
// Monitor sends alerts. The condition does not delete a previously created group.
resource alertActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (deployApps && !empty(alertEmail)) {
  name: '${containerAppEnvironmentName}-alerts'
  location: 'Global'
  properties: {
    enabled: true
    groupShortName: 'mortgages'
    emailReceivers: [
      {
        name: 'operator'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
  tags: appTags
}

// Optionally check workspace logs every five minutes and email the action group
// when more than four matching HTTP 5xx log entries occur in a five-minute
// window. This paid alert depends on log ingestion and is not an uptime monitor.
resource serverErrorAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (deployApps && !empty(alertEmail)) {
  name: '${containerAppEnvironmentName}-server-errors'
  location: location
  kind: 'LogAlert'
  properties: {
    displayName: 'Mortgage demo: repeated HTTP server errors'
    description: 'More than four HTTP 5xx responses across the apps within five minutes.'
    enabled: true
    severity: 2
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [logWorkspace.id]
    skipQueryValidation: true
    criteria: {
      allOf: [
        {
          query: '''
ContainerAppConsoleLogs_CL
| where Log_s contains "HTTP " and Log_s matches regex @"returned 5[0-9]{2} in "
'''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 4
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [alertActionGroup!.id]
    }
  }
  tags: appTags
}

// Host the apps and bootstrap job in a consumption environment on the delegated
// subnet, and send their console logs to the workspace. The environment is not
// internal-only; each app's ingress setting determines whether it is public.
resource containerEnvironment 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: containerAppEnvironmentName
  location: location
  properties: {
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    vnetConfiguration: {
      infrastructureSubnetId: appSubnet.id
      internal: false
    }
	appLogsConfiguration: {
	  destination: 'log-analytics'
	  logAnalyticsConfiguration: {
		customerId: logWorkspace.properties.customerId
		sharedKey: logWorkspace.listKeys().primarySharedKey
	  }
	}
  }
  tags: appTags
  dependsOn: [
    blobPrivateEndpoint
  ]
}

// When requested, define a manually triggered job inside the environment.
// It uses the pull identity for its image and the SQL admin identity to create
// users, migrate all four databases and copy historical simulation records.
// The deployment script starts and waits for it; declaring the job alone does
// not run it. Its arguments identify the intended runtime managed identities.
resource databaseBootstrap 'Microsoft.App/jobs@2025-01-01' = if (deployBootstrap) {
  name: '${containerAppEnvironmentName}-bootstrap'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${imagePullIdentity.id}': {}
      '${bootstrapIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: imagePullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'database-bootstrap'
          image: '${registry.properties.loginServer}/database-bootstrap:${imageTag}'
          // Pass the SQL server and runtime identities to the job. The job
          // uses these values to create users with the intended permissions.
          args: [
            '--server'
            sqlServer.properties.fullyQualifiedDomainName
            '--mortgages-identity-name'
            mortgagesIdentity.name
            '--mortgages-identity-client-id'
            mortgagesIdentity.properties.clientId
            '--products-identity-name'
            productsIdentity.name
            '--products-identity-client-id'
            productsIdentity.properties.clientId
            '--customer-sessions-identity-name'
            customerSessionsIdentity.name
            '--customer-sessions-identity-client-id'
            customerSessionsIdentity.properties.clientId
            '--notifications-identity-name'
            notificationsIdentity.name
            '--notifications-identity-client-id'
            notificationsIdentity.properties.clientId
          ]
          env: [
            // Choose the admin identity when Azure SDK requests a token.
            { name: 'AZURE_CLIENT_ID', value: bootstrapIdentity.properties.clientId }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
    }
  }
  tags: appTags
  dependsOn: [
    imagePullRole
    sqlEntraOnly
    mortgagesDatabase
    productsDatabase
    customerSessionsDatabase
    notificationsDatabase
    sqlPrivateDnsGroup
    sqlPrivateDnsLink
  ]
}

// Run the separate queue consumer without HTTP ingress. The worker identity
// receives messages and writes simulations to its own SQL database; no email is
// sent. The separate scaler identity has namespace-scoped Data Owner access
// and lifecycle None keeps it unavailable to the container. KEDA is configured
// for 0-1 replicas and one queued message; actual Azure behavior is unverified.
resource notificationsWorker 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: 'notifications-worker'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${imagePullIdentity.id}': {}
      '${notificationsIdentity.id}': {}
      '${notificationsScalerIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [{ server: registry.properties.loginServer, identity: imagePullIdentity.id }]
      identitySettings: [
        // The platform may use these identities for scaling and image pulls,
        // but lifecycle None prevents the application container from using them.
        { identity: notificationsScalerIdentity.id, lifecycle: 'None' }
        { identity: imagePullIdentity.id, lifecycle: 'None' }
      ]
    }
    template: {
      containers: [{
        name: 'notifications-worker'
        image: '${registry.properties.loginServer}/notifications-worker:${imageTag}'
        env: [
          // Double underscores map to nested .NET configuration settings.
          // Select the worker identity, queue and encrypted SQL connection.
          { name: 'AZURE_CLIENT_ID', value: notificationsIdentity.properties.clientId }
          { name: 'Notifications__Enabled', value: 'true' }
          { name: 'Notifications__ServiceBusNamespace', value: notifications.outputs.fullyQualifiedNamespace }
          { name: 'Notifications__QueueName', value: notifications.outputs.queueName }
          { name: 'ConnectionStrings__notifications', value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=notifications;Authentication=Active Directory Managed Identity;User Id=${notificationsIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
        ]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
      }]
      scale: {
        // Zero replicas reduces idle compute; one active message requests a
        // replica, capped at one. This scale rule requires live Azure testing.
        minReplicas: 0
        maxReplicas: 1
        rules: [{
          name: 'servicebus'
          custom: {
            type: 'azure-servicebus'
            identity: notificationsScalerIdentity.id
            metadata: {
              namespace: notifications.outputs.namespaceName
              queueName: notifications.outputs.queueName
              messageCount: '1'
            }
          }
        }]
      }
    }
  }
  tags: appTags
  dependsOn: [imagePullRole, notificationsDatabase]
}

// Run the Products API with internal HTTPS ingress, its own SQL identity and
// the shared Products key. Health probes check readiness and liveness; one to
// two replicas are configured, so at least one can incur ongoing compute costs.
resource productsApi 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: productsApiAppName
  location: location
  identity: {
	type: 'UserAssigned'
	userAssignedIdentities: {
	  '${imagePullIdentity.id}': {}
	  '${productsIdentity.id}': {}
	}
  }
  properties: {
	managedEnvironmentId: containerEnvironment.id
	configuration: {
	  activeRevisionsMode: 'Single'
	  registries: [
		{
		  server: registry.properties.loginServer
		  identity: imagePullIdentity.id
		}
	  ]
	  secrets: [
		{
		  name: 'product-service-api-key'
		  value: productServiceApiKey
		}
	  ]
	  ingress: {
		external: false
		allowInsecure: false
		targetPort: 8080
		transport: 'auto'
	  }
	}
	template: {
	  containers: [
		{
		  name: 'products-api'
		  image: '${registry.properties.loginServer}/products-api:${imageTag}'
		  env: [
            // Optional tracing and forwarded headers configure web telemetry.
            // The remaining settings select the identity, shared key and
            // encrypted Products SQL connection; bootstrap handles migrations.
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
			{
			  name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
			  value: 'true'
			}
			{
			  name: 'AZURE_CLIENT_ID'
			  value: productsIdentity.properties.clientId
			}
			{
			  name: 'ProductService__ApiKey'
			  secretRef: 'product-service-api-key'
			}
			{
			  name: 'Persistence__Provider'
			  value: 'SqlServer'
			}
			{
			  name: 'Persistence__RunMigrationsOnStartup'
			  value: 'false'
			}
			{
			  name: 'ConnectionStrings__products'
			  value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=products;Authentication=Active Directory Managed Identity;User Id=${productsIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
			}
		  ]
		  resources: {
			cpu: json('0.5')
			memory: '1Gi'
		  }
		  probes: [
			// Readiness controls traffic routing; liveness detects a stuck app.
			{
			  type: 'Readiness'
			  httpGet: {
				path: '/health/ready'
				port: 8080
				scheme: 'HTTP'
			  }
			  periodSeconds: 10
			  failureThreshold: 3
			}
			{
			  type: 'Liveness'
			  httpGet: {
				path: '/health/live'
				port: 8080
				scheme: 'HTTP'
			  }
			  periodSeconds: 30
			  failureThreshold: 3
			}
		  ]
		}
	  ]
	  scale: {
		minReplicas: 1
		maxReplicas: 2
	  }
	}
  }
  tags: appTags
  dependsOn: [
	imagePullRole
	documentStorageRole
	dataProtectionStorageRole
	sqlEntraAdmin
	sqlEntraOnly
	mortgagesDatabase
	productsDatabase
  ]
}

// Run the stateless Eligibility API with internal HTTPS ingress and its own
// shared key. It has no SQL database; health probes and 1-2 replicas are set.
resource eligibilityApi 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: eligibilityApiAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${imagePullIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [{ server: registry.properties.loginServer, identity: imagePullIdentity.id }]
      secrets: [{ name: 'eligibility-service-api-key', value: eligibilityServiceApiKey }]
      ingress: { external: false, allowInsecure: false, targetPort: 8080, transport: 'auto' }
    }
    template: {
      containers: [{
        name: 'eligibility-api'
        image: '${registry.properties.loginServer}/eligibility-api:${imageTag}'
        env: [
          // Select production settings, optional tracing and the API's secret.
          { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
          { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
          { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
          { name: 'EligibilityService__ApiKey', secretRef: 'eligibility-service-api-key' }
        ]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        probes: [
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 30, failureThreshold: 3 }
        ]
      }]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
  tags: appTags
  dependsOn: [imagePullRole]
}

// Run the internal Mortgage API with its managed identity, private SQL and Blob
// access, Entra token settings, and URLs and keys for the other APIs. It publishes
// outbox messages to Service Bus; the separate worker consumes them.
resource mortgageApi 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: mortgageApiAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${imagePullIdentity.id}': {}
      '${mortgagesIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registry.properties.loginServer
          identity: imagePullIdentity.id
        }
      ]
      secrets: [
        {
          name: 'product-service-api-key'
          value: productServiceApiKey
        }
        {
          name: 'eligibility-service-api-key'
          value: eligibilityServiceApiKey
        }
      ]
      ingress: {
        external: false
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'mortgages-api'
          image: '${registry.properties.loginServer}/mortgages-api:${imageTag}'
          env: [
            // Configure tracing, production proxy headers and the identity.
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: mortgagesIdentity.properties.clientId }
            // These IDs identify the trusted tenant, API and calling apps.
            { name: 'AzureAd__TenantId', value: tenant().tenantId }
            { name: 'AzureAd__ClientId', value: mortgageApiClientId }
            { name: 'MortgageApi__CustomerClientId', value: customerSessionsIdentity.properties.clientId }
            { name: 'MortgageApi__StaffClientId', value: staffClientId }
            // Call the internal APIs over HTTPS using their separate secrets.
            { name: 'ProductService__UseMock', value: 'false' }
            { name: 'ProductService__BaseUrl', value: 'https://${productsApi!.properties.configuration.ingress.fqdn}/' }
            { name: 'ProductService__ApiKey', secretRef: 'product-service-api-key' }
            { name: 'EligibilityService__BaseUrl', value: 'https://${eligibilityApi!.properties.configuration.ingress.fqdn}/' }
            { name: 'EligibilityService__ApiKey', secretRef: 'eligibility-service-api-key' }
            // Use encrypted SQL with managed identity; bootstrap runs migrations.
            { name: 'Persistence__Provider', value: 'SqlServer' }
            { name: 'Persistence__RunMigrationsOnStartup', value: 'false' }
            { name: 'ConnectionStrings__mortgages', value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=mortgages;Authentication=Active Directory Managed Identity;User Id=${mortgagesIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
            // Publish to the queue and mark this deployment as using the
            // separate worker, so scripts can reject legacy in-place upgrades.
            { name: 'Notifications__Enabled', value: 'true' }
            { name: 'Notifications__WorkerModel', value: 'separate' }
            { name: 'Notifications__ServiceBusNamespace', value: notifications.outputs.fullyQualifiedNamespace }
            { name: 'Notifications__QueueName', value: notifications.outputs.queueName }
            // Store uploaded documents in the private Blob container.
            { name: 'DocumentStorage__Provider', value: 'AzureBlob' }
            { name: 'DocumentStorage__BlobServiceUri', value: 'https://${documentStorage.name}.blob.${environment().suffixes.storage}/' }
            { name: 'DocumentStorage__ContainerName', value: 'mortgage-documents' }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          probes: [
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 30, failureThreshold: 3 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
  tags: appTags
  dependsOn: [
    imagePullRole
    documentStorageRole
    mortgagesDatabase
  ]
}

// Expose staff web publicly over HTTPS. Entra sign-in and delegated API tokens
// still protect staff operations; its secret is stored as a Container Apps
// secret and its managed identity accesses only the staff key container.
resource staffWeb 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: staffWebAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${imagePullIdentity.id}': {}
      '${staffIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registry.properties.loginServer
          identity: imagePullIdentity.id
        }
      ]
      secrets: [
        { name: 'staff-client-secret', value: staffClientSecret }
      ]
      ingress: {
        external: true
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'mortgages-staff-web'
          image: '${registry.properties.loginServer}/mortgages-staff-web:${imageTag}'
          env: [
            // Configure telemetry, the staff identity and Entra sign-in.
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: staffIdentity.properties.clientId }
            { name: 'AzureAd__TenantId', value: tenant().tenantId }
            { name: 'AzureAd__ClientId', value: staffClientId }
            { name: 'AzureAd__ClientSecret', secretRef: 'staff-client-secret' }
            // Request a delegated API scope and persist staff protection keys.
            { name: 'MortgageApi__BaseUrl', value: 'https://${mortgageApi!.properties.configuration.ingress.fqdn}/' }
            { name: 'MortgageApi__Scope', value: 'api://${mortgageApiClientId}/Mortgage.Staff' }
            { name: 'DataProtection__BlobUri', value: 'https://${documentStorage.name}.blob.${environment().suffixes.storage}/staff-data-protection/keys.xml' }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          probes: [
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 30, failureThreshold: 3 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  tags: appTags
  dependsOn: [
    imagePullRole
    staffDataProtectionStorageRole
  ]
}

// Expose the customer web app publicly over HTTPS. Its managed identity obtains
// API tokens and accesses its separate session database and protection keys;
// the web app does not directly access mortgage application data in SQL.
resource mortgagesWeb 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: mortgagesWebAppName
  location: location
  identity: {
	type: 'UserAssigned'
	userAssignedIdentities: {
	  '${imagePullIdentity.id}': {}
	  '${customerSessionsIdentity.id}': {}
	}
  }
  properties: {
	managedEnvironmentId: containerEnvironment.id
	configuration: {
	  activeRevisionsMode: 'Single'
	  registries: [
		{
		  server: registry.properties.loginServer
		  identity: imagePullIdentity.id
		}
	  ]
	  ingress: {
		external: true
		allowInsecure: false
		targetPort: 8080
		transport: 'auto'
	  }
	}
	template: {
	  containers: [
		{
		  name: 'mortgages-web'
		  image: '${registry.properties.loginServer}/mortgages-web:${imageTag}'
		  env: [
            // Configure telemetry and production proxy handling, then select
            // the customer identity and request its application API scope.
            // The remaining settings use private SQL sessions and Blob keys.
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
			{
			  name: 'ASPNETCORE_ENVIRONMENT'
			  value: 'Production'
			}
			{
			  name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
			  value: 'true'
			}
			{
			  name: 'AZURE_CLIENT_ID'
			  value: customerSessionsIdentity.properties.clientId
			}
			{
			  name: 'MortgageApi__BaseUrl'
			  value: 'https://${mortgageApi!.properties.configuration.ingress.fqdn}/'
			}
			{
			  name: 'MortgageApi__Scope'
			  value: 'api://${mortgageApiClientId}/.default'
			}
			{
			  name: 'Session__Provider'
			  value: 'SqlServer'
			}
			{
			  name: 'ConnectionStrings__customersessions'
			  value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=customer-sessions;Authentication=Active Directory Managed Identity;User Id=${customerSessionsIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
			}
			{
			  name: 'DataProtection__BlobUri'
			  value: 'https://${documentStorage.name}.blob.${environment().suffixes.storage}/data-protection/keys.xml'
			}
		  ]
		  resources: {
			cpu: json('0.5')
			memory: '1Gi'
		  }
		  probes: [
			{
			  type: 'Readiness'
			  httpGet: {
				path: '/health/ready'
				port: 8080
				scheme: 'HTTP'
			  }
			  periodSeconds: 10
			  failureThreshold: 3
			}
			{
			  type: 'Liveness'
			  httpGet: {
				path: '/health/live'
				port: 8080
				scheme: 'HTTP'
			  }
			  periodSeconds: 30
			  failureThreshold: 3
			}
		  ]
		}
	  ]
	  scale: {
		minReplicas: 1
		maxReplicas: 1
	  }
	}
  }
  tags: appTags
}

// Outputs return identifiers to the deployment scripts; they do not themselves
// deploy anything. Conditional app/job outputs are empty in earlier phases.
// The registry host lets scripts build and refer to images.
output registryLoginServer string = registry.properties.loginServer
// The customer URL is displayed for browser checks after app deployment.
output mortgagesWebUrl string = deployApps ? 'https://${mortgagesWeb!.properties.configuration.ingress.fqdn}' : ''
// The internal Products URL identifies the service inside the environment.
output productsApiInternalUrl string = deployApps ? 'https://${productsApi!.properties.configuration.ingress.fqdn}' : ''
// The internal Mortgage URL identifies the service inside the environment.
output mortgageApiInternalUrl string = deployApps ? 'https://${mortgageApi!.properties.configuration.ingress.fqdn}' : ''
// The staff URL is used for browser checks and Entra callback configuration.
output staffWebUrl string = deployApps ? 'https://${staffWeb!.properties.configuration.ingress.fqdn}' : ''
// The SQL host lets operators and scripts identify the logical server.
output databaseServerName string = sqlServer.properties.fullyQualifiedDomainName
// The job name lets the script start and monitor database preparation.
output databaseBootstrapJobName string = deployBootstrap ? databaseBootstrap!.name : ''
// The Mortgage identity name identifies the runtime identity to scripts.
output mortgagesIdentityName string = mortgagesIdentity.name
// Its client ID selects the identity when requesting managed-identity tokens.
output mortgagesIdentityClientId string = mortgagesIdentity.properties.clientId
// Its principal/object ID identifies it in Azure role assignments.
output mortgagesIdentityPrincipalId string = mortgagesIdentity.properties.principalId
// The queue resource ID lets the scripts verify or remove the old receiver role.
output notificationQueueId string = notifications.outputs.queueId
// The Products identity name identifies its runtime identity to scripts.
output productsIdentityName string = productsIdentity.name
// Its client ID selects the identity for token requests.
output productsIdentityClientId string = productsIdentity.properties.clientId
// The customer identity name identifies the retained identity to scripts.
output customerSessionsIdentityName string = customerSessionsIdentity.name
// Its client ID selects the identity for API and SQL token requests.
output customerSessionsIdentityClientId string = customerSessionsIdentity.properties.clientId
// Its principal/object ID identifies it when checking the Entra API role.
output customerSessionsIdentityPrincipalId string = customerSessionsIdentity.properties.principalId
