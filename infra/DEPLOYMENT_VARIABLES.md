# Azure deployment configuration

The [Azure deployment guide](../AZURE_DEPLOYMENT.md) is the authoritative
procedure. This page summarizes the values used by the quickstart and the
application-only GitHub Actions workflow.

## Interactive quickstart

The quickstart reads the current subscription and signed-in Entra user, requires
an existing resource group, and uses that group's location. It prompts for:

| Value | Meaning |
| --- | --- |
| Resource group | Existing group in the selected subscription |
| Container registry | Globally unique registry name; reuse it when retrying |
| SQL server | Globally unique logical server name; reuse it when retrying |
| Storage account | Globally unique lowercase storage account name; reuse it when retrying |
| Mortgage API client ID | Application/client ID of the API app registration |
| Staff web client ID | Application/client ID of the staff app registration |
| Staff web client secret | Secret **value** belonging to that staff registration |
| Products and Eligibility keys | Two different 32–128 character keys using letters, digits, underscores or hyphens |

Generate each API key with `openssl rand -hex 32` and retain it securely for
retries. Do not commit credentials or paste them into logs or chat. The
quickstart creates a restricted temporary parameters file and removes it on
normal exit; it does not persist the credentials for you.

It uses `mortgage-demo-env` as the Container Apps environment, and names the
five apps `mortgages-web`, `staff-web`, `mortgage-api`, `products-api` and
`eligibility-api`. The bootstrap job is `mortgage-demo-env-bootstrap`. The
GitHub deployment script currently expects those names.

## GitHub Actions variables

Configure these as repository **variables** under GitHub Settings → Secrets and
variables → Actions. They are identifiers, not credentials:

| Variable | Meaning |
| --- | --- |
| `AZURE_CLIENT_ID` | Client ID of the GitHub deployment identity |
| `AZURE_TENANT_ID` | Tenant containing the deployment identity |
| `AZURE_SUBSCRIPTION_ID` | Subscription containing the prototype |
| `AZURE_RESOURCE_GROUP` | Existing prototype resource group |
| `AZURE_CONTAINER_REGISTRY` | Existing registry **name**, not its login server URL |

The workflow uses an Entra federated credential for this repository's immutable
GitHub OIDC subject instead of a client secret; it does not consume the staff
secret or service keys and does not update them. Environment-scoped GitHub
variables are not available to this workflow because its job does not select
an environment.
See the [GitHub Actions setup](../AZURE_DEPLOYMENT.md#deploy-application-changes-with-github-actions)
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

The example parameters file is illustrative; it does not run the required
bootstrap or configure Entra role assignments by itself. Never put real
secrets in a committed parameters file. Reapplying the full template to an
existing deployment can overwrite app settings, secrets, images and tags.
