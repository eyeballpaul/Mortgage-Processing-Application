targetScope = 'resourceGroup'

param name string
param location string
param environmentId string
param pullIdentityId string
param registryServer string
param imageTag string
param imageRef string = ''
param tags object
param otlpEndpoint string
param otlpProtocol string

@secure()
param eligibilityServiceApiKey string

resource eligibilityApi 'Microsoft.App/containerApps@2026-07-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${pullIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [{ server: registryServer, identity: pullIdentityId }]
      secrets: [{ name: 'eligibility-service-api-key', value: eligibilityServiceApiKey }]
      ingress: { external: false, allowInsecure: false, targetPort: 8080, transport: 'auto' }
    }
    template: {
      containers: [{
        name: 'eligibility-api'
        image: empty(imageRef) ? '${registryServer}/eligibility-api:${imageTag}' : imageRef
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
  tags: tags
}

output fqdn string = eligibilityApi.properties.configuration.ingress.fqdn
