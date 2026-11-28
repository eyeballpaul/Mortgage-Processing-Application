// Deploy the private Products API and its SQL-backed catalog connection.
// Shared SQL, registry and managed identities are created by main.bicep.
targetScope = 'resourceGroup'

// Registry host and pull identity used to fetch the app image without credentials.
param registryServer string
param pullIdentityId string
// Shared Container Apps environment and runtime identity settings.
param environmentId string
param productsIdentityClientId string
// Private SQL host and identity resource ID for the Products database.
param sqlServerFqdn string
param productsIdentityId string
// Container App name, shared service key, and optional tracing configuration.
param appName string
@secure()
param productServiceApiKey string
param otlpEndpoint string
param otlpProtocol string
// Use imageTag for ordinary deployments or imageRef for an immutable digest.
param imageTag string
param imageRef string = ''
// Resource tags and Azure deployment region.
param appTags object
param location string

// Create one internal HTTPS Container App. Its own identity accesses Products
// SQL; the shared key authenticates calls from the Mortgage API.
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
	// Place the app in the shared environment and retain only one active revision.
	managedEnvironmentId: environmentId
	configuration: {
	  activeRevisionsMode: 'Single'
	  // Pull the image using a separate identity from the runtime SQL identity.
	  registries: [
		{
		  server: registryServer
		  identity: pullIdentityId
		}
	  ]
	  // Keep the shared service key out of ordinary environment-variable values.
	  secrets: [
		{
		  name: 'product-service-api-key'
		  value: productServiceApiKey
		}
	  ]
	  // Internal ingress allows service-to-service traffic without public access.
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
		  // Prefer a digest-pinned image when one is provided by release tooling.
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
			// A modest fixed allocation keeps the interview prototype small.
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
		// Keep one instance running for readiness and cap scale-out at two.
		minReplicas: 1
		maxReplicas: 2
	  }
	}
  }
  tags: appTags
}

// Return the internal DNS host for calls from Mortgage API.
output fqdn string = productsApi.properties.configuration.ingress.fqdn
