// Deploy the stateless, internal Eligibility API. It has no database; Mortgage
// API supplies fictional inputs and stores the resulting decision itself.
targetScope = 'resourceGroup'

// Container App name and deployment region.
param name string
param location string
// Existing environment, registry host and pull identity for the image.
param environmentId string
param pullIdentityId string
param registryServer string
// Use imageTag for ordinary deployments or imageRef for an immutable digest.
param imageTag string
param imageRef string = ''
// Common resource tags and optional OTLP tracing configuration.
param tags object
param otlpEndpoint string
param otlpProtocol string

// Shared secure key: the Mortgage API must be configured with the matching value.
@secure()
param eligibilityServiceApiKey string

// Run a single internal HTTPS app with no database or public ingress.
resource eligibilityApi 'Microsoft.App/containerApps@2026-07-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${pullIdentityId}': {} }
  }
  properties: {
    // Use the common environment and a single active revision per release.
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      // The registry pull identity is not used for application authorization.
      registries: [{ server: registryServer, identity: pullIdentityId }]
      // Store the shared API key as a platform secret.
      secrets: [{ name: 'eligibility-service-api-key', value: eligibilityServiceApiKey }]
      // Internal HTTPS only; clients reach this app through the environment.
      ingress: { external: false, allowInsecure: false, targetPort: 8080, transport: 'auto' }
    }
    template: {
      containers: [{
        name: 'eligibility-api'
        // Prefer immutable digest references when the release workflow supplies one.
        image: empty(imageRef) ? '${registryServer}/eligibility-api:${imageTag}' : imageRef
        env: [
          // Production hosting, optional telemetry, and authentication for API callers.
          { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
          { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
          { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: otlpProtocol }
          { name: 'EligibilityService__ApiKey', secretRef: 'eligibility-service-api-key' }
        ]
        // Use the smallest configured CPU/memory allocation for this stateless API.
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        // Container Apps uses readiness for routing and liveness for restart decisions.
        probes: [
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 30, failureThreshold: 3 }
        ]
      }]
      // Keep a replica ready and allow at most one extra during demand.
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
  tags: tags
}

// Return this internal host for Mortgage API's configured service URL.
output fqdn string = eligibilityApi.properties.configuration.ingress.fqdn
