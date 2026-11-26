// Provisions SQL, private document objects, Container Apps, and the customer web and internal Products API.
targetScope = 'resourceGroup'

@description('Azure region for the deployment.')
param location string = resourceGroup().location

@description('Globally unique, lowercase Azure Container Registry name.')
@minLength(5)
@maxLength(50)
param containerRegistryName string

@description('Container Apps managed environment name.')
param containerAppEnvironmentName string

@description('Globally unique Azure SQL logical server name.')
@minLength(1)
@maxLength(63)
param sqlServerName string

@description('Globally unique, lowercase storage account name (3-24 alphanumeric characters).')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Globally unique, lowercase storage account name for private mortgage document blobs.')
@minLength(3)
@maxLength(24)
param documentStorageAccountName string

@description('Name of the customer mortgage web Container App.')
param mortgagesWebAppName string

@description('Name of the Products API Container App.')
param productsApiAppName string

@description('Image tag produced by the pipeline.')
param imageTag string = 'latest'

@description('Object ID of the Azure DevOps service connection service principal.')
param pipelinePrincipalId string

@description('Display name of the Azure DevOps service connection service principal.')
param pipelinePrincipalName string

@description('Bootstrap SQL administrator password. SQL authentication is disabled after server provisioning.')
@secure()
param sqlAdminPassword string

@description('Random API key shared by the internal Products API and its callers. Supply securely.')
@secure()
param productServiceApiKey string

@description('Deploy the application Container Apps after their images have been pushed.')
param deployApps bool = true

var acrPushRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var appTags = {
  application: 'pd-mortgages-demo'
  managedBy: 'bicep'
}

// Admin credentials are disabled; CI push and ACA pulls use scoped Azure RBAC identities.
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

resource pipelinePushRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pipelinePrincipalId, acrPushRoleDefinitionId)
  scope: registry
  properties: {
	roleDefinitionId: acrPushRoleDefinitionId
	principalId: pipelinePrincipalId
	principalType: 'ServicePrincipal'
  }
}

resource imagePullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-pull'
  location: location
  tags: appTags
}

resource mortgagesIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-mortgages'
  location: location
  tags: appTags
}

resource productsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-products'
  location: location
  tags: appTags
}

resource imagePullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, imagePullIdentity.id, acrPullRoleDefinitionId)
  scope: registry
  properties: {
	roleDefinitionId: acrPullRoleDefinitionId
	principalId: imagePullIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

// Kept intact for exporting records from an earlier Azure Files demo deployment.
resource fileStorage 'Microsoft.Storage/storageAccounts@2026-09-01' = {
  name: storageAccountName
  location: location
  sku: {
	name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
	accessTier: 'Hot'
	allowBlobPublicAccess: false
	allowSharedKeyAccess: true
	minimumTlsVersion: 'TLS1_2'
	publicNetworkAccess: 'Enabled'
	supportsHttpsTrafficOnly: true
  }
  tags: appTags
}

resource legacyFileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2026-09-01' = {
  name: '${fileStorage.name}/default/mortgage-data'
  properties: {
	accessTier: 'TransactionOptimized'
	shareQuota: 100
  }
}

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
	publicNetworkAccess: 'Enabled'
	supportsHttpsTrafficOnly: true
  }
  tags: appTags
}

resource documentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${documentStorage.name}/default/mortgage-documents'
  properties: {
	publicAccess: 'None'
  }
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${documentStorage.name}/default/data-protection'
  properties: {
	publicAccess: 'None'
  }
}

resource documentStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(documentContainer.id, mortgagesIdentity.id, 'Storage Blob Data Contributor')
  scope: documentContainer
  properties: {
	roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
	principalId: mortgagesIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

resource dataProtectionStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionContainer.id, mortgagesIdentity.id, 'Storage Blob Data Contributor')
  scope: dataProtectionContainer
  properties: {
	roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
	principalId: mortgagesIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  properties: {
	administratorLogin: 'mortgageSqlAdmin'
	administratorLoginPassword: sqlAdminPassword
	publicNetworkAccess: 'Enabled'
	minimalTlsVersion: '1.2'
  }
  tags: appTags
}

resource sqlEntraAdmin 'Microsoft.Sql/servers/administrators@2022-05-01-preview' = {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
	administratorType: 'ActiveDirectory'
	login: pipelinePrincipalName
	sid: pipelinePrincipalId
	tenantId: tenant().tenantId
  }
}

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

resource sqlAzureServicesFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
	startIpAddress: '0.0.0.0'
	endIpAddress: '0.0.0.0'
  }
}

resource mortgagesDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'mortgages'
  location: location
  sku: {
	name: 'GP_S_Gen5_2'
	tier: 'GeneralPurpose'
	family: 'Gen5'
	capacity: 2
  }
  kind: 'v12.0,user,vcore,serverless'
  properties: {
	autoPauseDelay: 60
	minCapacity: json('0.5')
  }
  tags: appTags
}

resource productsDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'products'
  location: location
  sku: {
	name: 'GP_S_Gen5_2'
	tier: 'GeneralPurpose'
	family: 'Gen5'
	capacity: 2
  }
  kind: 'v12.0,user,vcore,serverless'
  properties: {
	autoPauseDelay: 60
	minCapacity: json('0.5')
  }
  tags: appTags
}

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

resource containerEnvironment 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: containerAppEnvironmentName
  location: location
  properties: {
	appLogsConfiguration: {
	  destination: 'log-analytics'
	  logAnalyticsConfiguration: {
		customerId: logWorkspace.properties.customerId
		sharedKey: logWorkspace.listKeys().primarySharedKey
	  }
	}
  }
  tags: appTags
}

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
	sqlAzureServicesFirewall
	mortgagesDatabase
	productsDatabase
  ]
}

resource mortgagesWeb 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: mortgagesWebAppName
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
			  value: mortgagesIdentity.properties.clientId
			}
			{
			  name: 'ApplicationSecurity__RequireAuthenticatedUser'
			  value: 'true'
			}
			{
			  name: 'ProductService__UseMock'
			  value: 'false'
			}
			{
			  name: 'ProductService__BaseUrl'
			  value: 'https://${productsApi!.properties.configuration.ingress.fqdn}/'
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
			  name: 'ConnectionStrings__mortgages'
			  value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=mortgages;Authentication=Active Directory Managed Identity;User Id=${mortgagesIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
			}
			{
			  name: 'Session__Provider'
			  value: 'SqlServer'
			}
			{
			  name: 'DocumentStorage__Provider'
			  value: 'AzureBlob'
			}
			{
			  name: 'DocumentStorage__BlobServiceUri'
			  value: 'https://${documentStorage.name}.blob.${environment().suffixes.storage}/'
			}
			{
			  name: 'DocumentStorage__ContainerName'
			  value: 'mortgage-documents'
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

output registryLoginServer string = registry.properties.loginServer
output mortgagesWebUrl string = deployApps ? 'https://${mortgagesWeb!.properties.configuration.ingress.fqdn}' : ''
output productsApiInternalUrl string = deployApps ? 'https://${productsApi!.properties.configuration.ingress.fqdn}' : ''
output databaseServerName string = sqlServer.properties.fullyQualifiedDomainName
output mortgagesIdentityName string = mortgagesIdentity.name
output mortgagesIdentityPrincipalId string = mortgagesIdentity.properties.principalId
output productsIdentityName string = productsIdentity.name
output productsIdentityPrincipalId string = productsIdentity.properties.principalId
