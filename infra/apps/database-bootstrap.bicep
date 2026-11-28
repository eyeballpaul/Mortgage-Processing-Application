// Define the manually triggered Container Apps Job used to create SQL runtime
// users, apply migrations to all databases and copy historical simulation rows.
// Deploying this job does not run it; deployment scripts start and monitor it.
targetScope = 'resourceGroup'

// Registry host and pull identity let the job fetch its image without a password.
param registryServer string
param pullIdentityId string
// Shared Container Apps environment.
param environmentId string
// Client IDs/names of the runtime identities for SQL user provisioning.
param mortgagesIdentityClientId string
param productsIdentityClientId string
param customerSessionsIdentityClientId string
param notificationsIdentityClientId string
param outboxPublisherIdentityClientId string
// Client ID selects the bootstrap identity when the job requests SQL tokens.
param bootstrapIdentityClientId string
// SQL host, runtime identity names, job name and image selection.
param sqlServerFqdn string
param mortgagesIdentityName string
param productsIdentityName string
param customerSessionsIdentityName string
param notificationsIdentityName string
param outboxPublisherIdentityName string
// Resource ID attaches the bootstrap identity that has temporary SQL admin rights.
param bootstrapIdentityId string
param environmentName string
param imageTag string
param imageRef string = ''
// Common resource tags and Azure deployment region.
param appTags object
param location string

// Create a one-off job with no ingress. A manual trigger prevents deployments
// from accidentally running migrations until the release workflow requests it.
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
    // Use the shared Container Apps environment and its Consumption profile.
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      // Only an explicit manual start runs this job; no schedule or event trigger is configured.
      triggerType: 'Manual'
      // Bound the runtime and disable automatic retries so operators inspect failures.
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
          // Prefer an immutable image digest when supplied by release tooling.
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
            '--outbox-publisher-identity-name'
            outboxPublisherIdentityName
            '--outbox-publisher-identity-client-id'
            outboxPublisherIdentityClientId
          ]
          env: [
            // Select the temporary SQL administrator identity for Azure SDK tokens.
            { name: 'AZURE_CLIENT_ID', value: bootstrapIdentityClientId }
          ]
          // Give the migration job more memory and CPU than the runtime workers.
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
    }
  }
  tags: appTags
}

// Return the job's deterministic name so scripts can start and inspect its execution.
output jobName string = databaseBootstrap.name
