// This module describes Service Bus resources in the selected resource group.
// main.bicep calls it during full deployment; the existing-app script can also
// deploy it separately. The namespace has a public endpoint, not a private one.
targetScope = 'resourceGroup'

// Create the namespace in the group's region unless the caller supplies one.
@description('Azure region for the Service Bus namespace.')
param location string = resourceGroup().location

// The caller supplies the existing publisher identity name for sender access.
@description('User-assigned managed identity used by the continuously running outbox publisher.')
param outboxPublisherIdentityName string

// The caller supplies the existing worker identity name for receiver access.
@description('Managed identity used by the notification worker.')
param notificationsIdentityName string

// The caller supplies the distinct identity used by the scale rule for metrics.
@description('Managed identity used only by the Service Bus scale rule.')
param notificationsScalerIdentityName string

// Derive a stable namespace name from the group ID so retries use the same
// name; Azure still requires namespace names to be globally unique.
var namespaceName = 'mortgage-sb-${uniqueString(resourceGroup().id)}'
// Resolve Azure's built-in Service Bus Data Sender role for the publisher.
var senderRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
// Resolve the built-in Data Receiver role for the worker.
var receiverRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')
// Resolve Data Owner for the scaler. It is much broader than metric reading:
// it can manage entities and send/receive messages. Review the deployment guide.
var ownerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419')

// Reference the publisher identity so it can send outbox events to the queue.
resource outboxPublisherIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: outboxPublisherIdentityName
}

// Reference the existing worker identity so it can receive queue messages.
resource notificationsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: notificationsIdentityName
}

// Reference the separate existing scaler identity for namespace metrics access.
resource notificationsScalerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: notificationsScalerIdentityName
}

// Create a Basic Service Bus namespace in the group. Basic has a public endpoint;
// local/SAS authentication is disabled, so callers must use Entra permissions.
// TLS 1.2 is the minimum, and usage can incur charges.
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
  tags: {
    application: 'pd-mortgages-demo'
    managedBy: 'bicep'
  }
}

// Create the submission queue inside the namespace. Messages expire after
// 14 days and go to the dead-letter queue; repeated failures dead-letter after
// the maximum delivery count of 10. Dead letters need operator attention.
resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'submission-notifications'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
  }
}

// Grant the publisher identity the sender role on this queue. The deterministic
// GUID makes reapplying it repeatable; other assignments may also grant access.
resource sender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, outboxPublisherIdentity.id, senderRole)
  properties: {
    principalId: outboxPublisherIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: senderRole
  }
}

// Allow the worker identity to receive from this queue, not the whole namespace.
resource receiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, notificationsIdentity.id, receiverRole)
  properties: {
    principalId: notificationsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: receiverRole
  }
}

// Grant the scaler Data Owner at namespace scope for the configured KEDA
// managed-identity approach. This powerful role is broader than receiver;
// main.bicep keeps the scaler identity unavailable to application containers.
// The permission and scale-from-zero behavior still need live Azure validation.
resource scaler 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: serviceBus
  name: guid(serviceBus.id, notificationsScalerIdentity.id, ownerRole)
  properties: {
    principalId: notificationsScalerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: ownerRole
  }
}

// Return the namespace host without protocol, path or port: the API and worker
// the publisher and worker use it to connect with managed identities.
output fullyQualifiedNamespace string = first(split(replace(replace(serviceBus.properties.serviceBusEndpoint, 'https://', ''), '/', ''), ':'))
// Return the short namespace name for the Container Apps scaling rule.
output namespaceName string = serviceBus.name
// Return the queue name for publishers, consumers and the scaling rule.
output queueName string = queue.name
// Return the resource ID so deployment scripts can check legacy role assignments.
output queueId string = queue.id
