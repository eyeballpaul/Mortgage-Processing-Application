targetScope = 'resourceGroup'

param registryServer string
param pullIdentityId string
param environmentId string
param mortgageApiFqdn string
param staffIdentityId string
param staffIdentityClientId string
param storageName string
param appName string
param mortgageApiClientId string
param staffClientId string
@secure()
param staffClientSecret string
param otlpEndpoint string
param otlpProtocol string
param imageTag string
param imageRef string = ''
param appTags object
param location string

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
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registryServer
          identity: pullIdentityId
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
}

output fqdn string = staffWeb.properties.configuration.ingress.fqdn
