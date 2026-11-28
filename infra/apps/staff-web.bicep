// Deploy the public staff portal. Entra authentication and the Mortgage API
// enforce staff authorization; public ingress itself is not access control.
targetScope = 'resourceGroup'

// Registry host and pull identity used to fetch the staff image.
param registryServer string
param pullIdentityId string
// Shared Container Apps environment, internal Mortgage API URL, and staff key-store identity.
param environmentId string
param mortgageApiFqdn string
param staffIdentityId string
param staffIdentityClientId string
// Storage account name for the staff-only Data Protection key container.
param storageName string
// App name and Entra client IDs for the Mortgage API and Staff Web registrations.
param appName string
param mortgageApiClientId string
param staffClientId string
// Secret value used by the staff web app for its confidential Entra client.
@secure()
param staffClientSecret string
// Optional trace collector settings.
param otlpEndpoint string
param otlpProtocol string
// Use imageTag for ordinary deployments or imageRef for an immutable digest.
param imageTag string
param imageRef string = ''
// Common resource tags and Azure deployment region.
param appTags object
param location string

// Create the public staff Container App over HTTPS. Its assigned identities
// separate image pulling from access to staff Data Protection keys.
resource staffWeb 'Microsoft.App/containerApps@2026-07-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentityId}': {}
      '${staffIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      // Single mode keeps only the selected app revision active.
      activeRevisionsMode: 'Single'
      // Pull with the registry identity; the staff key identity is runtime-only.
      registries: [
        {
          server: registryServer
          identity: pullIdentityId
        }
      ]
      // The Entra client secret is stored by Container Apps and referenced below.
      secrets: [
        { name: 'staff-client-secret', value: staffClientSecret }
      ]
      // Public HTTPS serves staff browsers; Entra still protects staff operations.
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
          // Prefer the immutable digest supplied by release tooling.
          image: empty(imageRef) ? '${registryServer}/mortgages-staff-web:${imageTag}' : imageRef
          env: [
            // Configure telemetry, the staff identity and Entra sign-in.
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: staffIdentityClientId }
            { name: 'AzureAd__TenantId', value: tenant().tenantId }
            { name: 'AzureAd__ClientId', value: staffClientId }
            { name: 'AzureAd__ClientSecret', secretRef: 'staff-client-secret' }
            // Request a delegated API scope and persist staff protection keys.
            { name: 'MortgageApi__BaseUrl', value: 'https://${mortgageApiFqdn}/' }
            { name: 'MortgageApi__Scope', value: 'api://${mortgageApiClientId}/Mortgage.Staff' }
            { name: 'DataProtection__BlobUri', value: 'https://${storageName}.blob.${environment().suffixes.storage}/staff-data-protection/keys.xml' }
          ]
          // Use a modest fixed allocation for the prototype portal.
          resources: { cpu: json('0.5'), memory: '1Gi' }
          // Readiness controls ingress routing; liveness detects an unhealthy process.
          probes: [
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 30, failureThreshold: 3 }
          ]
        }
      ]
      // One always-on replica serves the prototype; no scale-out is configured.
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  tags: appTags
}

// Return the public ingress hostname for callback registration and readiness checks.
output fqdn string = staffWeb.properties.configuration.ingress.fqdn
