// Provisions the customer and staff web apps, internal APIs, SQL databases and private blobs.
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

@description('Globally unique, lowercase storage account name for private mortgage document blobs.')
@minLength(3)
@maxLength(24)
param documentStorageAccountName string

@description('Name of the customer mortgage web Container App.')
param mortgagesWebAppName string

@description('Name of the Products API Container App.')
param productsApiAppName string

@description('Name of the internal Eligibility API Container App.')
param eligibilityApiAppName string

@description('Name of the internal Mortgage API Container App.')
param mortgageApiAppName string

@description('Name of the staff web Container App.')
param staffWebAppName string

@description('Application/client ID of the Mortgage API Entra app registration.')
param mortgageApiClientId string

@description('Application/client ID of the staff web Entra app registration.')
param staffClientId string

@description('Client secret value for the staff web Entra app registration.')
@secure()
param staffClientSecret string

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

@description('Random API key shared by the internal Eligibility API and Mortgage API.')
@secure()
param eligibilityServiceApiKey string

@description('Deploy the application Container Apps after their images have been pushed.')
param deployApps bool = true

@description('Optional email for the paid Azure Monitor 5xx log alert; leave empty to disable it.')
param alertEmail string = ''

@description('Optional trusted OTLP collector endpoint for sampled traces; leave empty to disable export. Do not include credentials.')
param otlpEndpoint string = ''

@allowed(['grpc', 'http/protobuf'])
@description('OTLP transport protocol used when trace export is enabled.')
param otlpProtocol string = 'grpc'

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

resource customerSessionsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-customer-sessions'
  location: location
  tags: appTags
}

resource staffIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${containerAppEnvironmentName}-staff'
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

resource staffDataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${documentStorage.name}/default/staff-data-protection'
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
  name: guid(dataProtectionContainer.id, customerSessionsIdentity.id, 'Storage Blob Data Contributor')
  scope: dataProtectionContainer
  properties: {
	roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
	principalId: customerSessionsIdentity.properties.principalId
	principalType: 'ServicePrincipal'
  }
}

resource staffDataProtectionStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(staffDataProtectionContainer.id, staffIdentity.id, 'Storage Blob Data Contributor')
  scope: staffDataProtectionContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: staffIdentity.properties.principalId
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
  properties: {
	autoPauseDelay: 60
	minCapacity: json('0.5')
  }
  tags: appTags
}

resource customerSessionsDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'customer-sessions'
  location: location
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
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
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: mortgagesIdentity.properties.clientId }
            { name: 'AzureAd__TenantId', value: tenant().tenantId }
            { name: 'AzureAd__ClientId', value: mortgageApiClientId }
            { name: 'MortgageApi__CustomerClientId', value: customerSessionsIdentity.properties.clientId }
            { name: 'MortgageApi__StaffClientId', value: staffClientId }
            { name: 'ProductService__UseMock', value: 'false' }
            { name: 'ProductService__BaseUrl', value: 'https://${productsApi!.properties.configuration.ingress.fqdn}/' }
            { name: 'ProductService__ApiKey', secretRef: 'product-service-api-key' }
            { name: 'EligibilityService__BaseUrl', value: 'https://${eligibilityApi!.properties.configuration.ingress.fqdn}/' }
            { name: 'EligibilityService__ApiKey', secretRef: 'eligibility-service-api-key' }
            { name: 'Persistence__Provider', value: 'SqlServer' }
            { name: 'Persistence__RunMigrationsOnStartup', value: 'false' }
            { name: 'ConnectionStrings__mortgages', value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=mortgages;Authentication=Active Directory Managed Identity;User Id=${mortgagesIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
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
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: staffIdentity.properties.clientId }
            { name: 'AzureAd__TenantId', value: tenant().tenantId }
            { name: 'AzureAd__ClientId', value: staffClientId }
            { name: 'AzureAd__ClientSecret', secretRef: 'staff-client-secret' }
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

output registryLoginServer string = registry.properties.loginServer
output mortgagesWebUrl string = deployApps ? 'https://${mortgagesWeb!.properties.configuration.ingress.fqdn}' : ''
output productsApiInternalUrl string = deployApps ? 'https://${productsApi!.properties.configuration.ingress.fqdn}' : ''
output mortgageApiInternalUrl string = deployApps ? 'https://${mortgageApi!.properties.configuration.ingress.fqdn}' : ''
output staffWebUrl string = deployApps ? 'https://${staffWeb!.properties.configuration.ingress.fqdn}' : ''
output databaseServerName string = sqlServer.properties.fullyQualifiedDomainName
output mortgagesIdentityName string = mortgagesIdentity.name
output mortgagesIdentityClientId string = mortgagesIdentity.properties.clientId
output productsIdentityName string = productsIdentity.name
output productsIdentityClientId string = productsIdentity.properties.clientId
output customerSessionsIdentityName string = customerSessionsIdentity.name
output customerSessionsIdentityClientId string = customerSessionsIdentity.properties.clientId
output customerSessionsIdentityPrincipalId string = customerSessionsIdentity.properties.principalId
