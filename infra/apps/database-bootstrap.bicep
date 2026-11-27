targetScope = 'resourceGroup'

param registryServer string
param pullIdentityId string
param environmentId string
param mortgagesIdentityClientId string
param productsIdentityClientId string
param customerSessionsIdentityClientId string
param notificationsIdentityClientId string
param bootstrapIdentityClientId string
param sqlServerFqdn string
param mortgagesIdentityName string
param productsIdentityName string
param customerSessionsIdentityName string
param notificationsIdentityName string
param bootstrapIdentityId string
param environmentName string
param imageTag string
param imageRef string = ''
param appTags object
param location string

resource databaseBootstrap 'Microsoft.App/jobs@2025-01-01' = {
  name: '${environmentName}-bootstrap'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentityId}': {}
      '${bootstrapIdentityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: registryServer
          identity: pullIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'database-bootstrap'
          image: empty(imageRef) ? '${registryServer}/database-bootstrap:${imageTag}' : imageRef
          // Pass the SQL server and runtime identities to the job. The job
          // uses these values to create users with the intended permissions.
          args: [
            '--server'
            sqlServerFqdn
            '--mortgages-identity-name'
            mortgagesIdentityName
            '--mortgages-identity-client-id'
            mortgagesIdentityClientId
            '--products-identity-name'
            productsIdentityName
            '--products-identity-client-id'
            productsIdentityClientId
            '--customer-sessions-identity-name'
            customerSessionsIdentityName
            '--customer-sessions-identity-client-id'
            customerSessionsIdentityClientId
            '--notifications-identity-name'
            notificationsIdentityName
            '--notifications-identity-client-id'
            notificationsIdentityClientId
          ]
          env: [
            // Choose the admin identity when Azure SDK requests a token.
            { name: 'AZURE_CLIENT_ID', value: bootstrapIdentityClientId }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
    }
  }
  tags: appTags
}

output jobName string = databaseBootstrap.name
