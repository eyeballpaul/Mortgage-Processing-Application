// Deploy the public customer journey. The web app owns only customer sessions
// and Data Protection keys; mortgage application data stays behind Mortgage API.
targetScope = 'resourceGroup'

// Registry host and pull identity used to fetch this app's image.
param registryServer string
param pullIdentityId string
// Shared Container Apps environment and customer identity client ID.
param environmentId string
param customerSessionsIdentityClientId string
// Private SQL host for sessions, internal Mortgage API host, and attached identity resource ID.
param sqlServerFqdn string
param mortgageApiFqdn string
param customerSessionsIdentityId string
// Storage account name for this app's separate Data Protection key container.
param storageName string
// App name and Mortgage API client ID used to request the API token.
param appName string
param mortgageApiClientId string
// Optional distributed tracing settings.
param otlpEndpoint string
param otlpProtocol string
// Use imageTag for ordinary deployments or imageRef for an immutable digest.
param imageTag string
param imageRef string = ''
// Common resource tags and Azure deployment region.
param appTags object
param location string

// Expose the customer web app publicly over HTTPS while retaining a private
// connection to SQL, Blob and the internal Mortgage API.
resource mortgagesWeb 'Microsoft.App/containerApps@2026-07-01' = {
  name: appName
  location: location
  identity: {
	type: 'UserAssigned'
	userAssignedIdentities: {
	  '${pullIdentityId}': {}
	  '${customerSessionsIdentityId}': {}
	}
  }
  properties: {
	managedEnvironmentId: environmentId
	configuration: {
	  // Single mode makes the selected deployment revision the only active one.
	  activeRevisionsMode: 'Single'
	  // Pull the image with the dedicated identity; runtime uses the customer identity.
	  registries: [
		{
		  server: registryServer
		  identity: pullIdentityId
		}
	  ]
	  ingress: {
		// Public HTTPS is intentional for the customer browser journey.
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
		  // Use the immutable digest supplied by release tooling when available.
		  image: empty(imageRef) ? '${registryServer}/mortgages-web:${imageTag}' : imageRef
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
			  value: customerSessionsIdentityClientId
			}
			{
			  name: 'MortgageApi__BaseUrl'
			  value: 'https://${mortgageApiFqdn}/'
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
			  value: 'Server=tcp:${sqlServerFqdn},1433;Database=customer-sessions;Authentication=Active Directory Managed Identity;User Id=${customerSessionsIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
			}
			{
			  name: 'DataProtection__BlobUri'
			  value: 'https://${storageName}.blob.${environment().suffixes.storage}/data-protection/keys.xml'
			}
		  ]
		  resources: {
			cpu: json('0.5')
			memory: '1Gi'
		  }
		  // Readiness gates public traffic; liveness detects a stuck process.
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
		// One replica limits prototype compute; there is no configured scale-out.
		minReplicas: 1
		maxReplicas: 1
	  }
	}
  }
  tags: appTags
}

// Return the public ingress hostname for display and readiness checks.
output fqdn string = mortgagesWeb.properties.configuration.ingress.fqdn
