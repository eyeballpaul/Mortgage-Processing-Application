# Azure deployment documentation has moved

This page remains for existing links. Choose the guide for your task:

- [Set up Azure from scratch](docs/azure-setup-from-scratch.md).
- [Set up the on-demand workflows](docs/azure-on-demand-setup.md).
- [Create, delete and recreate the prototype](AZURE_PROTOTYPE_GUIDE.md).
- [Deploy app, database and infrastructure changes](AZURE_PROTOTYPE_GUIDE.md#release-changes-to-an-existing-prototype).
- [Advanced deployment and operations](docs/azure-advanced-deployment.md).

## Choose where to start

See [choose where to start](AZURE_PROTOTYPE_GUIDE.md#choose-where-to-start).

## On-demand GitHub workflows

See the [prototype lifecycle guide](AZURE_PROTOTYPE_GUIDE.md#on-demand-github-workflows).

### One-time setup

See [one-time setup](docs/azure-on-demand-setup.md#one-time-setup).

#### 1. Check what must remain

See [retained resources and the customer identity](docs/azure-on-demand-setup.md#1-check-what-must-remain).

#### 2. Allow the deployment identity to manage Azure resources

See [Azure permissions](docs/azure-on-demand-setup.md#2-allow-the-deployment-identity-to-manage-azure-resources).
For the separate worker, see
[outbox publisher and notification identities and permissions](docs/azure-on-demand-setup.md#add-the-outbox-publisher-and-notification-identities-and-permissions).

#### 3. Add Microsoft Graph permissions and grant consent

See [Graph permissions and consent](docs/azure-on-demand-setup.md#3-add-microsoft-graph-permissions-and-grant-consent).

#### 4. Make the deployment principal an owner of Staff Web

See [Staff Web ownership](docs/azure-on-demand-setup.md#4-make-the-deployment-principal-an-owner-of-staff-web).

#### 5. Store GitHub repository variables and secrets

See [GitHub configuration](docs/azure-on-demand-setup.md#5-store-github-repository-variables-and-secrets).

### Run

See [run the workflows](AZURE_PROTOTYPE_GUIDE.md#run-the-workflows).

### Run the workflows

See [run the workflows](AZURE_PROTOTYPE_GUIDE.md#run-the-workflows).

#### Create when needed

See [create when needed](AZURE_PROTOTYPE_GUIDE.md#create-when-needed).

#### Delete when finished

See [delete when finished](AZURE_PROTOTYPE_GUIDE.md#delete-when-finished).

#### Workflow troubleshooting

See [workflow troubleshooting](AZURE_PROTOTYPE_GUIDE.md#workflow-troubleshooting).

## Before deploying

See [before deploying](docs/azure-advanced-deployment.md#before-deploying).

## Submission notifications

See [submission notifications](docs/azure-advanced-deployment.md#submission-notifications).

### Enable on an existing quickstart deployment

See [enable notifications](docs/azure-advanced-deployment.md#enable-on-an-existing-quickstart-deployment).

### One-time GitHub deployment permission

See [GitHub deployment permission](docs/azure-advanced-deployment.md#one-time-github-deployment-permission).

### Monitoring, retries and cost

See [notification monitoring, retries and cost](docs/azure-advanced-deployment.md#monitoring-retries-and-cost).

## Deploy

See [interactive deployment](docs/azure-advanced-deployment.md#deploy).

### Staff sign-in troubleshooting

See [staff sign-in troubleshooting](docs/azure-advanced-deployment.md#staff-sign-in-troubleshooting).

## Deploy application changes with GitHub Actions

See [application deployment](docs/azure-advanced-deployment.md#deploy-application-changes-with-github-actions).

### GitHub deployment troubleshooting

See [GitHub deployment troubleshooting](docs/azure-advanced-deployment.md#github-deployment-troubleshooting).

### After deleting the resource group

See [resource-group recreation](AZURE_PROTOTYPE_GUIDE.md#after-deleting-the-resource-group).

## When finished

See [when finished](AZURE_PROTOTYPE_GUIDE.md#when-finished).
