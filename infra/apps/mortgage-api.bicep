targetScope = 'resourceGroup'

param registryServer string
param pullIdentityId string
param environmentId string
param mortgagesIdentityClientId string
param customerSessionsIdentityClientId string
param sqlServerFqdn string
param serviceBusFqdn string
param serviceBusQueueName string
param productsApiFqdn string
param eligibilityApiFqdn string
param mortgagesIdentityId string
param storageName string
param appName string
param mortgageApiClientId string
param staffClientId string
@secure()
param productServiceApiKey string
@secure()
param eligibilityServiceApiKey string
param otlpEndpoint string
param otlpProtocol string
param imageTag string
param imageRef string = ''
param appTags object
param location string

resource mortgageApi 'Microsoft.App/containerApps@2026-07-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentityId}': {}
      '${mortgagesIdentityId}': {}
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
          image: empty(imageRef) ? '${registryServer}/mortgages-api:${imageTag}' : imageRef
          env: [
            // Configure tracing, production proxy headers and the identity.
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: mortgagesIdentityClientId }
            // These IDs identify the trusted tenant, API and calling apps.
            { name: 'AzureAd__TenantId', value: tenant().tenantId }
            { name: 'AzureAd__ClientId', value: mortgageApiClientId }
            { name: 'MortgageApi__CustomerClientId', value: customerSessionsIdentityClientId }
            { name: 'MortgageApi__StaffClientId', value: staffClientId }
            // Call the internal APIs over HTTPS using their separate secrets.
            { name: 'ProductService__UseMock', value: 'false' }
            { name: 'ProductService__BaseUrl', value: 'https://${productsApiFqdn}/' }
            { name: 'ProductService__ApiKey', secretRef: 'product-service-api-key' }
            { name: 'EligibilityService__BaseUrl', value: 'https://${eligibilityApiFqdn}/' }
            { name: 'EligibilityService__ApiKey', secretRef: 'eligibility-service-api-key' }
            // Use encrypted SQL with managed identity; bootstrap runs migrations.
            { name: 'Persistence__Provider', value: 'SqlServer' }
            { name: 'Persistence__RunMigrationsOnStartup', value: 'false' }
            { name: 'ConnectionStrings__mortgages', value: 'Server=tcp:${sqlServerFqdn},1433;Database=mortgages;Authentication=Active Directory Managed Identity;User Id=${mortgagesIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
            // Publish to the queue and mark this deployment as using the
            // separate worker, so scripts can reject legacy in-place upgrades.
            { name: 'Notifications__Enabled', value: 'true' }
            { name: 'Notifications__WorkerModel', value: 'separate' }
            { name: 'Notifications__ServiceBusNamespace', value: serviceBusFqdn }
            { name: 'Notifications__QueueName', value: serviceBusQueueName }
            // Store uploaded documents in the private Blob container.
            { name: 'DocumentStorage__Provider', value: 'AzureBlob' }
            { name: 'DocumentStorage__BlobServiceUri', value: 'https://${storageName}.blob.${environment().suffixes.storage}/' }
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
}

output fqdn string = mortgageApi.properties.configuration.ingress.fqdn
