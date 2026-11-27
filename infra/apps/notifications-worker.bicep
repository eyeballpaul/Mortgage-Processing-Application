targetScope = 'resourceGroup'

param registryServer string
param pullIdentityId string
param environmentId string
param notificationsIdentityClientId string
param sqlServerFqdn string
param serviceBusFqdn string
param serviceBusName string
param serviceBusQueueName string
param notificationsScalerIdentityId string
param notificationsIdentityId string
param imageTag string
param imageRef string = ''
param appTags object
param location string

resource notificationsWorker 'Microsoft.App/containerApps@2026-07-01' = {
  name: 'notifications-worker'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentityId}': {}
      '${notificationsIdentityId}': {}
      '${notificationsScalerIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [{ server: registryServer, identity: pullIdentityId }]
      identitySettings: [
        // The platform may use these identities for scaling and image pulls,
        // but lifecycle None prevents the application container from using them.
        { identity: notificationsScalerIdentityId, lifecycle: 'None' }
        { identity: pullIdentityId, lifecycle: 'None' }
      ]
    }
    template: {
      containers: [{
        name: 'notifications-worker'
        image: empty(imageRef) ? '${registryServer}/notifications-worker:${imageTag}' : imageRef
        env: [
          // Double underscores map to nested .NET configuration settings.
          // Select the worker identity, queue and encrypted SQL connection.
          { name: 'AZURE_CLIENT_ID', value: notificationsIdentityClientId }
          { name: 'Notifications__Enabled', value: 'true' }
          { name: 'Notifications__ServiceBusNamespace', value: serviceBusFqdn }
          { name: 'Notifications__QueueName', value: serviceBusQueueName }
          { name: 'ConnectionStrings__notifications', value: 'Server=tcp:${sqlServerFqdn},1433;Database=notifications;Authentication=Active Directory Managed Identity;User Id=${notificationsIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
        ]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
      }]
      scale: {
        // Zero replicas reduces idle compute; one active message requests a
        // replica, capped at one. This scale rule requires live Azure testing.
        minReplicas: 0
        maxReplicas: 1
        rules: [{
          name: 'servicebus'
          custom: {
            type: 'azure-servicebus'
            identity: notificationsScalerIdentityId
            metadata: {
              namespace: serviceBusName
              queueName: serviceBusQueueName
              messageCount: '1'
            }
          }
        }]
      }
    }
  }
  tags: appTags
}
