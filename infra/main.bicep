// Provisions an authenticated ACR, Container Apps environment, persistent mortgage-file share,
// and the customer web and internal Products API apps. Designed for synthetic demo data only.
targetScope = 'resourceGroup'

@description('Azure region for the deployment.')
param location string = resourceGroup().location

@description('Globally unique, lowercase Azure Container Registry name.')
@minLength(5)
@maxLength(50)
param containerRegistryName string

@description('Container Apps managed environment name.')
param containerAppEnvironmentName string

@description('Globally unique, lowercase storage account name (3-24 alphanumeric characters).')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Name of the customer mortgage web Container App.')
param mortgagesWebAppName string

@description('Name of the Products API Container App.')
param productsApiAppName string

@description('Image tag produced by the pipeline.')
param imageTag string = 'latest'

@description('Object ID of the Azure DevOps service connection service principal.')
param pipelinePrincipalId string

@description('Random API key shared by the internal Products API and its callers. Supply securely.')
@secure()
param productServiceApiKey string

@description('Deploy the application Container Apps after their images have been pushed.')
param deployApps bool = true

var acrPushRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var storageShareName = 'mortgage-data'
var storageMountName = 'mortgage-data'
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

resource imagePullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, imagePullIdentity.id, acrPullRoleDefinitionId)
  scope: registry
  properties: {
	roleDefinitionId: acrPullRoleDefinitionId
	principalId: imagePullIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

// Azure Files is mounted by the customer app because the current repository stores
// mortgage application records and uploaded documents through its file repository.
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

resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2026-09-01' = {
  name: '${fileStorage.name}/default/${storageShareName}'
  properties: {
	accessTier: 'TransactionOptimized'
	shareQuota: 100
  }
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

resource environmentFileStorage 'Microsoft.App/managedEnvironments/storages@2026-07-01' = {
  parent: containerEnvironment
  name: storageMountName
  properties: {
	azureFile: {
	  accessMode: 'ReadWrite'
	  accountKey: fileStorage.listKeys().keys[0].value
	  accountName: fileStorage.name
	  shareName: storageShareName
	}
  }
  dependsOn: [
	fileShare
  ]
}

resource productsApi 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: productsApiAppName
  location: location
  identity: {
	type: 'UserAssigned'
	userAssignedIdentities: {
	  '${imagePullIdentity.id}': {}
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
			  name: 'ProductService__ApiKey'
			  secretRef: 'product-service-api-key'
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
	environmentFileStorage
  ]
}

resource mortgagesWeb 'Microsoft.App/containerApps@2026-07-01' = if (deployApps) {
  name: mortgagesWebAppName
  location: location
  identity: {
	type: 'UserAssigned'
	userAssignedIdentities: {
	  '${imagePullIdentity.id}': {}
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
			  value: 'File'
			}
			{
			  name: 'Persistence__RootPath'
			  value: '/app/data'
			}
			{
			  name: 'Session__Provider'
			  value: 'Memory'
			}
		  ]
		  resources: {
			cpu: json('0.5')
			memory: '1Gi'
		  }
		  volumeMounts: [
			{
			  volumeName: storageMountName
			  mountPath: '/app/data'
			}
		  ]
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
	  volumes: [
		{
		  name: storageMountName
		  storageType: 'AzureFile'
		  storageName: environmentFileStorage.name
		  mountOptions: 'uid=1654,gid=1654,dir_mode=0770,file_mode=0660'
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
