targetScope = 'resourceGroup'

param registryServer string
param pullIdentityId string
param environmentId string
param productsIdentityClientId string
param sqlServerFqdn string
param productsIdentityId string
param appName string
@secure()
param productServiceApiKey string
param otlpEndpoint string
param otlpProtocol string
param imageTag string
param imageRef string = ''
param appTags object
param location string

resource productsApi 'Microsoft.App/containerApps@2026-07-01' = {
  name: appName
  location: location
  identity: {
	type: 'UserAssigned'
	userAssignedIdentities: {
	  '${pullIdentityId}': {}
	  '${productsIdentityId}': {}
	}
  }
  properties: {
	managedEnvironmentId: environmentId
	configuration: {
	  activeRevisionsMode: 'Single'
	  registries: [
		{
		  server: registryServer
		  identity: pullIdentityId
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
		  image: empty(imageRef) ? '${registryServer}/products-api:${imageTag}' : imageRef
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
			  value: productsIdentityClientId
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
			  value: 'Server=tcp:${sqlServerFqdn},1433;Database=products;Authentication=Active Directory Managed Identity;User Id=${productsIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
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
}

output fqdn string = productsApi.properties.configuration.ingress.fqdn
