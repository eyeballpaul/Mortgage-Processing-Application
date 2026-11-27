# Azure deployment configuration

The [prototype lifecycle guide](../AZURE_PROTOTYPE_GUIDE.md) covers routine
use; the [advanced deployment guide](../docs/azure-advanced-deployment.md)
covers the quickstart. This page summarizes their configuration values.

## How to use this page

1. Follow the [first-time setup guide](../docs/azure-setup-from-scratch.md) if
   you have not deployed before.
2. Before running the quickstart, use the first table below to gather its
   inputs. Keep secrets in a password manager, not in this file.
3. Before running an on-demand workflow, add the GitHub values in the second
   table and complete the linked permission checks. A variable is an
   identifier; a secret is a credential whose value must not be shared.
4. On retries, use the same resource names and matching secret values unless
   you have deliberately planned a change.

For the on-demand GitHub create and delete workflows, including the additional
repository variables, secrets, retained identities and required permissions,
see [on-demand setup](../docs/azure-on-demand-setup.md).

## Interactive quickstart

The quickstart reads the current subscription and signed-in Entra user, and
creates a missing resource group after confirmation. It prompts for:

| Value | What to enter and where to find it |
| --- | --- |
| Resource group and region | Existing or new group name; the script asks for a region only for a new group |
| Container registry | Globally unique registry name; find an existing name in the resource group and reuse it when retrying |
| SQL server | Globally unique logical server name; find an existing name in the resource group and reuse it when retrying |
| Storage account | Globally unique lowercase storage account name; find an existing name in the resource group and reuse it when retrying |
| Mortgage API client ID | Entra → App registrations → Mortgage API → Overview → Application (client) ID |
| Staff web client ID | Entra → App registrations → Staff Web → Overview → Application (client) ID |
| Staff web client secret | Secret **value** saved when created under Staff Web → Certificates & secrets; not its secret ID |
| Products and Eligibility keys | Two different 32–128 character keys using letters, digits, underscores or hyphens; retain existing keys for retries |
| GitHub setup and repository | Optional automatic setup for an existing deployment app and `owner/name` repository; answer `n` if prerequisites are missing |

Generate each API key with `openssl rand -hex 32` and retain it securely for
retries. Do not commit credentials or paste them into logs or chat. The
quickstart creates a restricted temporary parameters file and removes it on
normal exit; it does not persist the credentials for you.
Automatic GitHub setup requires `gh`, authentication and repository variable
management access, plus existing `AZURE_CLIENT_ID` and `AZURE_TENANT_ID`
repository variables and the deployment app's federated credential.
The quickstart grants Azure roles, updates the subscription, group and registry
repository variables, and adds staff callbacks to Entra. Its optional
queue-scoped condition covers only the Mortgage API identity and is not
sufficient for the worker and scaler; complete the
[group-scoped setup and notification extension](../docs/azure-on-demand-setup.md#2-allow-the-deployment-identity-to-manage-azure-resources).
Choose manual GitHub setup on first-time deployment if those prerequisites do
not yet exist.

It uses `mortgage-demo-env` as the Container Apps environment, and names the
six apps `mortgages-web`, `staff-web`, `mortgage-api`, `products-api`,
`eligibility-api` and `notifications-worker`. The bootstrap job is `mortgage-demo-env-bootstrap`. The
GitHub deployment script currently expects those names.

## GitHub Actions variables

1. In GitHub, open the repository → **Settings → Secrets and variables →
   Actions → Variables**.
2. Select **New repository variable**, add each name and value below, then
   select **Add variable**. Update existing entries rather than duplicating
   them. They are identifiers, not credentials.
3. The first five are also used by the regular application deployment.
   The on-demand workflows require the remaining values as well.

| Variable | Where to find its value |
| --- | --- |
| `AZURE_CLIENT_ID` | GitHub deployment app registration → Application (client) ID |
| `AZURE_TENANT_ID` | That registration → Directory (tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | Azure Portal → Subscriptions → selected subscription → Subscription ID |
| `AZURE_RESOURCE_GROUP` | Dedicated prototype resource group name |
| `AZURE_CONTAINER_REGISTRY` | Registry **name**, not its login server URL |
| `AZURE_SQL_SERVER` | Prototype SQL server name |
| `AZURE_STORAGE_ACCOUNT` | Prototype storage account name |
| `MORTGAGE_API_CLIENT_ID` | Mortgage API app registration → Application (client) ID |
| `STAFF_CLIENT_ID` | Staff Web app registration → Application (client) ID |

4. Select **Secrets → New repository secret** and add `STAFF_CLIENT_SECRET`,
   `PRODUCTS_API_KEY` and `ELIGIBILITY_API_KEY` for the create workflow. Use
   the same staff secret value and matching service keys as the deployment.
   GitHub will not show their values after saving them. Never put them in
   variables, Git or chat.

The workflow uses an Entra federated credential for this repository's immutable
GitHub OIDC subject instead of a deployment client secret. The regular
application deployment does not consume or update the staff secret or service
keys; the on-demand create workflow does consume them. Environment-scoped
GitHub variables and secrets are not available because these jobs do not
select an environment.
See the [GitHub Actions setup](../docs/azure-advanced-deployment.md#deploy-application-changes-with-github-actions)
for permissions and recreation guidance.

## Bicep settings

[main.bicep](main.bicep) defines the infrastructure parameters. `createSqlServer`
is `true` when creating a SQL server and `false` when updating an existing one;
the quickstart selects this automatically. `deployApps` and `deployBootstrap`
control the corresponding resources. `alertEmail` and `otlpEndpoint` are
optional and can add costs.

`pipelinePrincipalId` is the Entra **object ID** of the user or service
principal granted registry push permission; it is not the subscription ID
returned by `az account show --query id`. `pipelinePrincipalType` must match
that identity. The quickstart supplies the signed-in user and sets the type to
`User`. No SQL administrator password is needed.

The quickstart also creates a public Basic Service Bus namespace and the
`submission-notifications` queue using [notifications.bicep](notifications.bicep).
The Mortgage API receives queue-scoped sender access, and the worker's separate
identity receives queue-scoped receiver access. A separate scaler-only identity
receives namespace-scoped Service Bus Data Owner; it is unavailable to the
containers. `Notifications__Enabled`, `Notifications__ServiceBusNamespace` and
`Notifications__QueueName` are set by the template; no Service Bus key is used.
The regular workflow applies this notification module and Mortgage API settings
to an existing compatible deployment. It cannot create a missing worker or
upgrade an API-hosted consumer; see the
[legacy cutover](../docs/azure-advanced-deployment.md#enable-on-an-existing-quickstart-deployment).
It requires the constrained group-scoped role-assignment permissions described
in the [deployment guide](../docs/azure-advanced-deployment.md#one-time-github-deployment-permission).

The example parameters file is illustrative; it does not run the required
bootstrap or configure Entra role assignments by itself. Never put real
secrets in a committed parameters file. Reapplying the full template to an
existing deployment can overwrite app settings, secrets, images and tags.
