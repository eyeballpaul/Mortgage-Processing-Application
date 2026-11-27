targetScope = 'resourceGroup'

param registryServer string
param pullIdentityId string
param environmentId string
param customerSessionsIdentityClientId string
param sqlServerFqdn string
param mortgageApiFqdn string
param customerSessionsIdentityId string
param storageName string
param appName string
param mortgageApiClientId string
param otlpEndpoint string
param otlpProtocol string
param imageTag string
param imageRef string = ''
param appTags object
param location string

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
	  activeRevisionsMode: 'Single'
	  registries: [
		{
		  server: registryServer
		  identity: pullIdentityId
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

output fqdn string = mortgagesWeb.properties.configuration.ingress.fqdn
