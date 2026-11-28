// Deploy the internal API that owns mortgage applications, audit data and
// document access. Shared infrastructure and its managed identities are
// provisioned by main.bicep; this module configures only the API Container App.
targetScope = 'resourceGroup'

// Registry host and pull identity let Container Apps fetch the private image without a password.
param registryServer string
param pullIdentityId string
// Shared Container Apps environment hosting this private-ingress API.
param environmentId string
// Client IDs select the API's SQL identity and the customer caller identity.
param mortgagesIdentityClientId string
param customerSessionsIdentityClientId string
// Private SQL host and internal URLs for the Products and Eligibility APIs.
param sqlServerFqdn string
param productsApiFqdn string
param eligibilityApiFqdn string
// Resource ID attaches the API's own managed identity to the container.
param mortgagesIdentityId string
// Storage account name used to form the private document Blob endpoint.
param storageName string
// Container App resource name and Entra API/staff app client IDs used for token validation.
param appName string
param mortgageApiClientId string
param staffClientId string
// These secure keys must match the corresponding downstream APIs.
@secure()
param productServiceApiKey string
@secure()
param eligibilityServiceApiKey string
// Optional trace collector settings; empty endpoint disables export.
param otlpEndpoint string
param otlpProtocol string
// Use imageTag for ordinary deployments, or imageRef for an immutable digest.
param imageTag string
param imageRef string = ''
// Common ownership and application tags, plus the Azure region for this app.
param appTags object
param location string

// Create one private-HTTPS Container App. Its managed identities are split
// between registry pulls and runtime access; no public ingress is exposed.
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
      // Single mode ensures a deployment leaves only one active revision.
      activeRevisionsMode: 'Single'
      // Pull the image using the dedicated registry identity.
      registries: [
        {
          server: registryServer
          identity: pullIdentityId
        }
      ]
      // Keep downstream service keys as Container Apps secrets, not plain env values.
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
      // Internal ingress permits calls from apps in the environment, not the public internet.
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
          // A supplied digest takes precedence over the mutable quickstart tag.
          image: empty(imageRef) ? '${registryServer}/mortgages-api:${imageTag}' : imageRef
          env: [
            // Configure tracing, production proxy headers and the identity.
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: mortgagesIdentityClientId }
            { name: 'Notifications__WorkerModel', value: 'separate' }
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
            // Store uploaded documents in the private Blob container.
            { name: 'DocumentStorage__Provider', value: 'AzureBlob' }
            { name: 'DocumentStorage__BlobServiceUri', value: 'https://${storageName}.blob.${environment().suffixes.storage}/' }
            { name: 'DocumentStorage__ContainerName', value: 'mortgage-documents' }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          // Readiness gates traffic; liveness detects a process that needs restarting.
          probes: [
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 30, failureThreshold: 3 }
          ]
        }
      ]
      // Keep one replica available for the prototype, with a small scale-out ceiling.
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
  tags: appTags
}

// Return the internal DNS host so the web apps and other APIs can call Mortgage API.
output fqdn string = mortgageApi.properties.configuration.ingress.fqdn
