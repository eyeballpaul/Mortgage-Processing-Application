// Deploy the queue consumer as a private background Container App. It records
// simulated notifications in SQL; it does not send email or expose HTTP ingress.
targetScope = 'resourceGroup'

// Registry and pull identity provide passwordless image retrieval.
param registryServer string
param pullIdentityId string
// Shared environment and worker runtime client ID for Entra token selection.
param environmentId string
param notificationsIdentityClientId string
// Private notifications SQL host and Service Bus namespace/queue settings.
param sqlServerFqdn string
param serviceBusFqdn string
param serviceBusName string
param serviceBusQueueName string
// The scaler's attached resource ID is separate from the worker's runtime identity.
param notificationsScalerIdentityId string
param notificationsIdentityId string
// Use imageTag for ordinary deployments or imageRef for an immutable digest.
param imageTag string
param imageRef string = ''
// Common resource tags and deployment region.
param appTags object
param location string

// Attach the pull, worker and scaler identities for distinct platform/runtime
// purposes; identitySettings below keeps privileged identities out of the app.
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
    // Use the shared environment and retain only one active worker revision.
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      // Pull the container image through the dedicated registry identity.
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
        // Prefer immutable image digests supplied by release tooling.
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
        // A small allocation is sufficient for the prototype's simulated work.
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
      }]
      // KEDA polls Service Bus with the scaler identity; a queued message wakes
      // the app from zero, and maxReplicas bounds prototype compute.
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
