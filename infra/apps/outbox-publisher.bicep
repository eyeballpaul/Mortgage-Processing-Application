// Deploy the continuous publisher that reads leased outbox rows from Mortgage
// SQL and sends them to Service Bus. This background app has no ingress and
// intentionally keeps one replica running to poll continuously.
targetScope = 'resourceGroup'

// Registry host and pull identity for passwordless image retrieval.
param registryServer string
param pullIdentityId string
// Shared Container Apps environment and publisher runtime identity.
param environmentId string
param outboxPublisherIdentityClientId string
param outboxPublisherIdentityId string
// Private SQL host and Service Bus namespace/queue used by the publisher.
param sqlServerFqdn string
param serviceBusFqdn string
param serviceBusQueueName string
// App name, image selection and resource metadata.
param environmentName string
param imageTag string
param imageRef string = ''
param appTags object
param location string

// Create one always-on, non-ingress Container App. Its separate user-assigned
// identity receives only the SQL outbox and queue sender permissions.
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
    // Place the publisher in the shared environment with a single active revision.
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      // Pull the image with the registry identity, not the queue sender identity.
      registries: [{ server: registryServer, identity: pullIdentityId }]
    }
    template: {
      containers: [{
        name: 'outbox-publisher'
        // Prefer the immutable digest supplied by release tooling.
        image: empty(imageRef) ? '${registryServer}/outbox-publisher:${imageTag}' : imageRef
        // Configure the publisher's Entra identity, queue destination and
        // continuous (not run-once) polling mode.
        env: [
          { name: 'AZURE_CLIENT_ID', value: outboxPublisherIdentityClientId }
          { name: 'Notifications__Enabled', value: 'true' }
          { name: 'Notifications__ServiceBusNamespace', value: serviceBusFqdn }
          { name: 'Notifications__QueueName', value: serviceBusQueueName }
          { name: 'OutboxPublisher__RunOnce', value: 'false' }
          { name: 'ConnectionStrings__mortgages', value: 'Server=tcp:${sqlServerFqdn},1433;Database=mortgages;Authentication=Active Directory Managed Identity;User Id=${outboxPublisherIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
        ]
        // Keep resource use small while leaving enough memory for the .NET worker.
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
      }]
      scale: {
        // A single continuously running replica checks the outbox every poll interval.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  tags: appTags
}

// Return the deterministic Container App name for deployment scripts.
output appName string = outboxPublisher.name
