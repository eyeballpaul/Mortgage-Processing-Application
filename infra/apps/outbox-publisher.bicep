targetScope = 'resourceGroup'

param registryServer string
param pullIdentityId string
param environmentId string
param outboxPublisherIdentityClientId string
param outboxPublisherIdentityId string
param sqlServerFqdn string
param serviceBusFqdn string
param serviceBusQueueName string
param environmentName string
param imageTag string
param imageRef string = ''
param appTags object
param location string

resource outboxPublisher 'Microsoft.App/containerApps@2026-07-01' = {
  name: '${environmentName}-outbox-publisher'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentityId}': {}
      '${outboxPublisherIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [{ server: registryServer, identity: pullIdentityId }]
    }
    template: {
      containers: [{
        name: 'outbox-publisher'
        image: empty(imageRef) ? '${registryServer}/outbox-publisher:${imageTag}' : imageRef
        env: [
          { name: 'AZURE_CLIENT_ID', value: outboxPublisherIdentityClientId }
          { name: 'Notifications__Enabled', value: 'true' }
          { name: 'Notifications__ServiceBusNamespace', value: serviceBusFqdn }
          { name: 'Notifications__QueueName', value: serviceBusQueueName }
          { name: 'OutboxPublisher__RunOnce', value: 'false' }
          { name: 'ConnectionStrings__mortgages', value: 'Server=tcp:${sqlServerFqdn},1433;Database=mortgages;Authentication=Active Directory Managed Identity;User Id=${outboxPublisherIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
        ]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
      }]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  tags: appTags
}

output appName string = outboxPublisher.name
