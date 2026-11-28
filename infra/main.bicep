// Bicep describes the desired Azure resources; Azure creates or updates them when
// the deployment scripts submit this file. It is not a sequence of shell commands.
// The scripts deploy the foundation, bootstrap job and apps in separate phases.
// See ../docs/azure-advanced-deployment.md before deploying: resources cost money,
// the worker has not yet been validated in Azure, and a budget does not cap costs.
// All resources below belong to the resource group selected by the deployment.
// Reading this file: param is an input, var is a reusable value, resource is an
// Azure object, module calls another Bicep file, and output returns a value.
// @description and other @ decorators document or validate inputs. A resource's
// quoted type includes its Azure API version. Its properties configure the
// object; parent and scope locate it, and dependsOn controls creation ordering.
// An "if" condition skips a declaration when false; ! asserts a conditional
// resource exists where the condition makes it safe to read.
targetScope = 'resourceGroup'

// Where resources are created; use the resource group's region unless overridden.
@description('Azure region for the deployment.')
param location string = resourceGroup().location

// The registry stores the container images; Azure requires this name to be globally
// unique. The decorators enforce the permitted length, not global uniqueness.
@description('Globally unique, lowercase Azure Container Registry name.')
@minLength(5)
@maxLength(50)
param containerRegistryName string

// Names the shared Container Apps environment hosting the services and job.
@description('Container Apps managed environment name.')
param containerAppEnvironmentName string

// Names the logical SQL server that contains all four separate databases.
@description('Globally unique Azure SQL logical server name.')
@minLength(1)
@maxLength(63)
param sqlServerName string

// Names the private storage account for documents and web-app protection keys.
@description('Globally unique, lowercase storage account name for private mortgage document blobs.')
@minLength(3)
@maxLength(24)
param documentStorageAccountName string

// Name used for the publicly reachable customer web app in Container Apps.
@description('Name of the customer mortgage web Container App.')
param mortgagesWebAppName string

// Name used for the internal product-catalog API.
@description('Name of the Products API Container App.')
param productsApiAppName string

// Name used for the internal eligibility API.
@description('Name of the internal Eligibility API Container App.')
param eligibilityApiAppName string

// Name used for the internal Mortgage API, which owns application data.
@description('Name of the internal Mortgage API Container App.')
param mortgageApiAppName string

// Name used for the publicly reachable staff web app.
@description('Name of the staff web Container App.')
param staffWebAppName string

// Identifies the existing Entra API registration, so apps can request API tokens.
@description('Application/client ID of the Mortgage API Entra app registration.')
param mortgageApiClientId string

// Identifies the existing Entra staff registration used for staff sign-in.
@description('Application/client ID of the staff web Entra app registration.')
param staffClientId string

// Staff sign-in uses this secret value. Supply it securely, never in source control;
// @secure prevents Azure from displaying it as an ordinary deployment parameter.
@description('Client secret value for the staff web Entra app registration.')
@secure()
param staffClientSecret string

// Selects which built images Container Apps runs; quickstart defaults to latest.
@description('Image tag produced by the pipeline.')
param imageTag string = 'latest'

// The directory object ID, not application client ID, allowed to push images.
@description('Object ID of the user or service principal granted AcrPush.')
param pipelinePrincipalId string

// Tells Azure whether the push principal is a person or application identity.
@allowed(['User', 'ServicePrincipal'])
@description('Directory principal type of the identity granted AcrPush.')
param pipelinePrincipalType string = 'ServicePrincipal'

// Include creation-only SQL administrator settings for a new server. The
// quickstart sets false for an existing server to avoid reapplying those settings.
@description('Set to true only when creating a new SQL server; false when redeploying an existing server.')
param createSqlServer bool = true

// Shared secret for Mortgage-to-Products calls; both apps must receive the same
// value. Supply it securely, not in a committed parameters file.
@description('Random API key shared by the internal Products API and its callers. Supply securely.')
@secure()
param productServiceApiKey string

// Separate shared secret for Mortgage-to-Eligibility calls; keep it distinct
// from the Products key and supply it securely.
@description('Random API key shared by the internal Eligibility API and Mortgage API.')
@secure()
param eligibilityServiceApiKey string

// The scripts set this after images and database preparation are ready; false
// allows the foundation to be deployed without starting application containers.
@description('Deploy the application Container Apps after their images have been pushed.')
param deployApps bool = true

// Enables the manually started SQL preparation job in its own deployment phase.
@description('Deploy the manual database bootstrap job after its image has been pushed.')
param deployBootstrap bool = false

// A nonempty address enables a paid alert after apps are deployed. Clearing it
// in an incremental deployment does not delete an alert that already exists.
@description('Optional email for the paid Azure Monitor 5xx log alert; leave empty to disable it.')
param alertEmail string = ''

// Optional external trace collector. An empty value leaves export disabled;
// an operator supplies and trusts the collector separately.
@description('Optional trusted OTLP collector endpoint for sampled traces; leave empty to disable export. Do not include credentials.')
param otlpEndpoint string = ''

// Match the trace transport to the collector; Azure accepts only these choices.
@allowed(['grpc', 'http/protobuf'])
@description('OTLP transport protocol used when trace export is enabled.')
param otlpProtocol string = 'grpc'

module foundation './foundation.bicep' = {
  name: 'mortgage-foundation-deployment'
  params: {
    location: location
    containerRegistryName: containerRegistryName
    containerAppEnvironmentName: containerAppEnvironmentName
    sqlServerName: sqlServerName
    documentStorageAccountName: documentStorageAccountName
    pipelinePrincipalId: pipelinePrincipalId
    pipelinePrincipalType: pipelinePrincipalType
    createSqlServer: createSqlServer
    deployAlerts: deployApps
    alertEmail: alertEmail
  }
}

// When requested, define a manually triggered job inside the environment.
// It uses the pull identity for its image and the SQL admin identity to create
// users, migrate all four databases and copy historical simulation records.
// The deployment script starts and waits for it; declaring the job alone does
// not run it. Its arguments identify the intended runtime managed identities.
module databaseBootstrap './apps/database-bootstrap.bicep' = if (deployBootstrap) {
  name: 'database-bootstrap-deployment'
  params: {
    registryServer: foundation.outputs.registryServer
    pullIdentityId: foundation.outputs.imagePullIdentityId
    environmentId: foundation.outputs.environmentId
    mortgagesIdentityClientId: foundation.outputs.mortgagesIdentityClientId
    productsIdentityClientId: foundation.outputs.productsIdentityClientId
    customerSessionsIdentityClientId: foundation.outputs.customerSessionsIdentityClientId
    notificationsIdentityClientId: foundation.outputs.notificationsIdentityClientId
    outboxPublisherIdentityClientId: foundation.outputs.outboxPublisherIdentityClientId
    bootstrapIdentityClientId: foundation.outputs.bootstrapIdentityClientId
    sqlServerFqdn: foundation.outputs.sqlServerFqdn
    mortgagesIdentityName: foundation.outputs.mortgagesIdentityName
    productsIdentityName: foundation.outputs.productsIdentityName
    customerSessionsIdentityName: foundation.outputs.customerSessionsIdentityName
    notificationsIdentityName: foundation.outputs.notificationsIdentityName
    outboxPublisherIdentityName: foundation.outputs.outboxPublisherIdentityName
    bootstrapIdentityId: foundation.outputs.bootstrapIdentityId
    environmentName: containerAppEnvironmentName
    imageTag: imageTag
    appTags: foundation.outputs.tags
    location: location
  }
}

// Run a continuously hosted worker for the transactional outbox. It receives
// only scoped SQL outbox permissions and Service Bus sender access.
module outboxPublisher './apps/outbox-publisher.bicep' = if (deployApps) {
  name: 'outbox-publisher-deployment'
  params: {
    registryServer: foundation.outputs.registryServer
    pullIdentityId: foundation.outputs.imagePullIdentityId
    environmentId: foundation.outputs.environmentId
    outboxPublisherIdentityClientId: foundation.outputs.outboxPublisherIdentityClientId
    outboxPublisherIdentityId: foundation.outputs.outboxPublisherIdentityId
    sqlServerFqdn: foundation.outputs.sqlServerFqdn
    serviceBusFqdn: foundation.outputs.serviceBusFqdn
    serviceBusQueueName: foundation.outputs.serviceBusQueueName
    environmentName: containerAppEnvironmentName
    imageTag: imageTag
    appTags: foundation.outputs.tags
    location: location
  }
}

// Run the separate queue consumer without HTTP ingress. The worker identity
// receives messages and writes simulations to its own SQL database; no email is
// sent. The separate scaler identity has namespace-scoped Data Owner access
// and lifecycle None keeps it unavailable to the container. KEDA is configured
// for 0-1 replicas and one queued message; actual Azure behavior is unverified.
module notificationsWorker './apps/notifications-worker.bicep' = if (deployApps) {
  name: 'notifications-worker-deployment'
  params: {
    registryServer: foundation.outputs.registryServer
    pullIdentityId: foundation.outputs.imagePullIdentityId
    environmentId: foundation.outputs.environmentId
    notificationsIdentityClientId: foundation.outputs.notificationsIdentityClientId
    sqlServerFqdn: foundation.outputs.sqlServerFqdn
    serviceBusFqdn: foundation.outputs.serviceBusFqdn
    serviceBusName: foundation.outputs.serviceBusName
    serviceBusQueueName: foundation.outputs.serviceBusQueueName
    notificationsScalerIdentityId: foundation.outputs.notificationsScalerIdentityId
    notificationsIdentityId: foundation.outputs.notificationsIdentityId
    imageTag: imageTag
    appTags: foundation.outputs.tags
    location: location
  }
}

// Run the Products API with internal HTTPS ingress, its own SQL identity and
// the shared Products key. Health probes check readiness and liveness; one to
// two replicas are configured, so at least one can incur ongoing compute costs.
module productsApi './apps/products-api.bicep' = if (deployApps) {
  name: 'products-api-deployment'
  params: {
    registryServer: foundation.outputs.registryServer
    pullIdentityId: foundation.outputs.imagePullIdentityId
    environmentId: foundation.outputs.environmentId
    productsIdentityClientId: foundation.outputs.productsIdentityClientId
    sqlServerFqdn: foundation.outputs.sqlServerFqdn
    productsIdentityId: foundation.outputs.productsIdentityId
    appName: productsApiAppName
    productServiceApiKey: productServiceApiKey
    otlpEndpoint: otlpEndpoint
    otlpProtocol: otlpProtocol
    imageTag: imageTag
    appTags: foundation.outputs.tags
    location: location
  }
}

// Run the stateless Eligibility API with internal HTTPS ingress and its own
// shared key. It has no SQL database; health probes and 1-2 replicas are set.
module eligibilityApi './apps/eligibility-api.bicep' = if (deployApps) {
  name: 'eligibility-api-deployment'
  params: {
    name: eligibilityApiAppName
    location: location
    environmentId: foundation.outputs.environmentId
    pullIdentityId: foundation.outputs.imagePullIdentityId
    registryServer: foundation.outputs.registryServer
    imageTag: imageTag
    tags: foundation.outputs.tags
    otlpEndpoint: otlpEndpoint
    otlpProtocol: otlpProtocol
    eligibilityServiceApiKey: eligibilityServiceApiKey
  }
}

// Run the internal Mortgage API with its managed identity, private SQL and Blob
// access, Entra token settings, and URLs and keys for the other APIs. The
// continuous outbox publisher app owns event publishing.
module mortgageApi './apps/mortgage-api.bicep' = if (deployApps) {
  name: 'mortgage-api-deployment'
  params: {
    registryServer: foundation.outputs.registryServer
    pullIdentityId: foundation.outputs.imagePullIdentityId
    environmentId: foundation.outputs.environmentId
    mortgagesIdentityClientId: foundation.outputs.mortgagesIdentityClientId
    customerSessionsIdentityClientId: foundation.outputs.customerSessionsIdentityClientId
    sqlServerFqdn: foundation.outputs.sqlServerFqdn
    productsApiFqdn: productsApi!.outputs.fqdn
    eligibilityApiFqdn: eligibilityApi!.outputs.fqdn
    mortgagesIdentityId: foundation.outputs.mortgagesIdentityId
    storageName: foundation.outputs.storageName
    appName: mortgageApiAppName
    mortgageApiClientId: mortgageApiClientId
    staffClientId: staffClientId
    productServiceApiKey: productServiceApiKey
    eligibilityServiceApiKey: eligibilityServiceApiKey
    otlpEndpoint: otlpEndpoint
    otlpProtocol: otlpProtocol
    imageTag: imageTag
    appTags: foundation.outputs.tags
    location: location
  }
}

// Expose staff web publicly over HTTPS. Entra sign-in and delegated API tokens
// still protect staff operations; its secret is stored as a Container Apps
// secret and its managed identity accesses only the staff key container.
module staffWeb './apps/staff-web.bicep' = if (deployApps) {
  name: 'staff-web-deployment'
  params: {
    registryServer: foundation.outputs.registryServer
    pullIdentityId: foundation.outputs.imagePullIdentityId
    environmentId: foundation.outputs.environmentId
    mortgageApiFqdn: mortgageApi!.outputs.fqdn
    staffIdentityId: foundation.outputs.staffIdentityId
    staffIdentityClientId: foundation.outputs.staffIdentityClientId
    storageName: foundation.outputs.storageName
    appName: staffWebAppName
    mortgageApiClientId: mortgageApiClientId
    staffClientId: staffClientId
    staffClientSecret: staffClientSecret
    otlpEndpoint: otlpEndpoint
    otlpProtocol: otlpProtocol
    imageTag: imageTag
    appTags: foundation.outputs.tags
    location: location
  }
}

// Expose the customer web app publicly over HTTPS. Its managed identity obtains
// API tokens and accesses its separate session database and protection keys;
// the web app does not directly access mortgage application data in SQL.
module mortgagesWeb './apps/mortgages-web.bicep' = if (deployApps) {
  name: 'mortgages-web-deployment'
  params: {
    registryServer: foundation.outputs.registryServer
    pullIdentityId: foundation.outputs.imagePullIdentityId
    environmentId: foundation.outputs.environmentId
    customerSessionsIdentityClientId: foundation.outputs.customerSessionsIdentityClientId
    sqlServerFqdn: foundation.outputs.sqlServerFqdn
    mortgageApiFqdn: mortgageApi!.outputs.fqdn
    customerSessionsIdentityId: foundation.outputs.customerSessionsIdentityId
    storageName: foundation.outputs.storageName
    appName: mortgagesWebAppName
    mortgageApiClientId: mortgageApiClientId
    otlpEndpoint: otlpEndpoint
    otlpProtocol: otlpProtocol
    imageTag: imageTag
    appTags: foundation.outputs.tags
    location: location
  }
}

// Outputs return identifiers to the deployment scripts; they do not themselves
// deploy anything. Conditional app/job outputs are empty in earlier phases.
// The registry host lets scripts build and refer to images.
output registryLoginServer string = foundation.outputs.registryServer
// The customer URL is displayed for browser checks after app deployment.
output mortgagesWebUrl string = deployApps ? 'https://${mortgagesWeb!.outputs.fqdn}' : ''
// The internal Products URL identifies the service inside the environment.
output productsApiInternalUrl string = deployApps ? 'https://${productsApi!.outputs.fqdn}' : ''
// The internal Mortgage URL identifies the service inside the environment.
output mortgageApiInternalUrl string = deployApps ? 'https://${mortgageApi!.outputs.fqdn}' : ''
// The staff URL is used for browser checks and Entra callback configuration.
output staffWebUrl string = deployApps ? 'https://${staffWeb!.outputs.fqdn}' : ''
// The SQL host lets operators and scripts identify the logical server.
output databaseServerName string = foundation.outputs.sqlServerFqdn
// The job name lets the script start and monitor database preparation.
output databaseBootstrapJobName string = deployBootstrap ? databaseBootstrap!.outputs.jobName : ''
// The app name identifies the continuously running outbox publisher.
output outboxPublisherAppName string = deployApps ? outboxPublisher!.outputs.appName : ''
// The Mortgage identity name identifies the runtime identity to scripts.
output mortgagesIdentityName string = foundation.outputs.mortgagesIdentityName
output outboxPublisherIdentityName string = foundation.outputs.outboxPublisherIdentityName
// Its client ID selects the identity when requesting managed-identity tokens.
output mortgagesIdentityClientId string = foundation.outputs.mortgagesIdentityClientId
output outboxPublisherIdentityClientId string = foundation.outputs.outboxPublisherIdentityClientId
// Its principal/object ID identifies it in Azure role assignments.
output mortgagesIdentityPrincipalId string = foundation.outputs.mortgagesIdentityPrincipalId
// The queue resource ID lets the scripts verify or remove the old receiver role.
output notificationQueueId string = foundation.outputs.serviceBusQueueId
// The Products identity name identifies its runtime identity to scripts.
output productsIdentityName string = foundation.outputs.productsIdentityName
// Its client ID selects the identity for token requests.
output productsIdentityClientId string = foundation.outputs.productsIdentityClientId
// The customer identity name identifies the retained identity to scripts.
output customerSessionsIdentityName string = foundation.outputs.customerSessionsIdentityName
// Its client ID selects the identity for API and SQL token requests.
output customerSessionsIdentityClientId string = foundation.outputs.customerSessionsIdentityClientId
// Its principal/object ID identifies it when checking the Entra API role.
output customerSessionsIdentityPrincipalId string = foundation.outputs.customerSessionsIdentityPrincipalId
