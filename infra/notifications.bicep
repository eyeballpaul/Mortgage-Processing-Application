targetScope = 'resourceGroup'

@description('Azure region for the Service Bus namespace.')
param location string = resourceGroup().location

@description('Existing user-assigned managed identity used by the Mortgage API.')
param mortgagesIdentityName string

@description('Managed identity used by the notification worker.')
param notificationsIdentityName string

@description('Managed identity used only by the Service Bus scale rule.')
param notificationsScalerIdentityName string

var namespaceName = 'mortgage-sb-${uniqueString(resourceGroup().id)}'
var senderRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var receiverRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')
var ownerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419')

resource mortgagesIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: mortgagesIdentityName
}

resource notificationsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: notificationsIdentityName
}

resource notificationsScalerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: notificationsScalerIdentityName
}

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

resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'submission-notifications'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
  }
}

resource sender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, mortgagesIdentity.id, senderRole)
  properties: {
    principalId: mortgagesIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: senderRole
  }
}

resource receiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, notificationsIdentity.id, receiverRole)
  properties: {
    principalId: notificationsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: receiverRole
  }
}

resource scaler 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: serviceBus
  name: guid(serviceBus.id, notificationsScalerIdentity.id, ownerRole)
  properties: {
    principalId: notificationsScalerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: ownerRole
  }
}

output fullyQualifiedNamespace string = first(split(replace(replace(serviceBus.properties.serviceBusEndpoint, 'https://', ''), '/', ''), ':'))
output namespaceName string = serviceBus.name
output queueName string = queue.name
output queueId string = queue.id
