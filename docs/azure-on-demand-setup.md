# One-time setup for Azure on-demand workflows

Follow this guide after the [first-time bootstrap or recovery](azure-setup-from-scratch.md).
It describes the GitHub identity, permissions and secrets used by repeated
create/delete runs and is also the manual verification reference for the local
setup scripts. When finished, use the
[prototype lifecycle guide](../AZURE_PROTOTYPE_GUIDE.md#run-the-workflows).
For day-to-day code changes, use the
[beginner's release guide](../AZURE_PROTOTYPE_GUIDE.md#release-changes-to-an-existing-prototype);
pushes run checks but do not build images or deploy.

## One-time setup

Do this once after the interactive quickstart. An Azure role administrator,
an Entra administrator and a GitHub repository administrator may be needed;
one person does not necessarily have all three permissions. If a button is
disabled or access is denied, ask the relevant administrator rather than
granting broader permissions.

### 1. Check what must remain

1. Open [Azure Portal](https://portal.azure.com) and select the correct
   directory and subscription. Go to **Resource groups**, then open your
   dedicated prototype group.
2. On **Resources**, look for these six existing **Managed Identity** resources:
   `mortgage-demo-env-pull`, `mortgage-demo-env-mortgages`,
   `mortgage-demo-env-products`, `mortgage-demo-env-customer-sessions`,
   `mortgage-demo-env-bootstrap` and `mortgage-demo-env-staff`.
3. If any are missing, complete the
   [interactive deployment](azure-advanced-deployment.md#deploy) first.
   The workflow requires these exact identities and the existing
   `MortgageCustomer` app-role assignment on customer-sessions. It checks
   that assignment but cannot create it. Do not delete the group or identities
   between normal runs. Two additional notification identities are created
   or verified in step 2 below.
4. Open **Microsoft Entra ID → App registrations** and confirm that the
   Mortgage API, Staff Web and GitHub deployment registrations already exist.
   Reuse them; see the [first-time guide](azure-setup-from-scratch.md)
   if you have not created them.

To verify the customer assignment, open `mortgage-demo-env-customer-sessions`
in the resource group and copy its **Principal ID**. In the Mortgage API
app registration, open **App roles** and note the ID of the enabled
`MortgageCustomer` role for **Applications**. In **Enterprise applications**,
open Mortgage API and note its **Object ID**. In Cloud Shell (Bash), replace
the placeholder with the customer identity's principal ID and run:

```bash
az rest --method get --url 'https://graph.microsoft.com/v1.0/servicePrincipals/<customer-principal-id>/appRoleAssignments'
```

In the `value` array, an assignment must have an `appRoleId` matching that
role ID and a `resourceId` matching the Mortgage API **enterprise
application** object ID. Follow `@odata.nextLink` if present. If it is
missing, complete the interactive quickstart using an appropriately authorized
user; do not grant an unrelated role or use the API client ID in its place.

### 2. Allow the deployment identity to manage Azure resources

**Why:** GitHub signs in as a service principal (the directory instance of
the deployment app). It needs Contributor to deploy, plus a restricted
permission to create the role assignments used by the prototype.

1. In the resource group, open **Access control (IAM) → Role assignments**.
   Find the existing GitHub deployment service principal and verify it has
   **Contributor** at the resource-group scope. If it does not, an Azure
   administrator can use **Add → Add role assignment → Contributor**,
   select **User, group, or service principal**, choose the deployment app,
   then **Review + assign**.
2. Find its group-scoped **Role Based Access Control Administrator**
   assignment. If it already exists, inspect its condition and preserve the
   intended permissions rather than adding a duplicate or replacing an
   extended condition. If the five-role, five-principal condition below is
   already present, skip to the notification identity section. Otherwise,
   an Azure role administrator must add or update the conditioned assignment
   for this same service principal at the resource-group scope. Review any
   existing conditions with the administrator before changing them.
3. In the condition editor choose the option for **selected roles and selected
   principals**, then **Constrain roles and principals**. Select exactly these
   five roles:
   `AcrPush`, `AcrPull`, `Storage Blob Data Contributor`,
   `Azure Service Bus Data Sender` and `Azure Service Bus Data Receiver`.
4. Select these five principals: the **GitHub deployment service principal**,
   plus the `mortgage-demo-env-pull`, `mortgage-demo-env-mortgages`,
   `mortgage-demo-env-customer-sessions` and `mortgage-demo-env-staff` managed
   identities. Select the objects themselves, not merely similarly named
   registrations. Use their **principal/object IDs**, not client IDs, when
   identifying them.
5. Apply the condition to both role-assignment **write** and **delete**
   actions, review the scope and selections, and save. Return to **Role
   assignments** and inspect the saved condition. The portal may use slightly
   different wording; see
   [Microsoft's condition examples](https://learn.microsoft.com/azure/role-based-access-control/delegate-role-assignments-examples#example-constrain-roles-and-specific-groups).

This initial condition allows *any* of the five roles for *any* of the five principals
within the group, not just specific role/principal pairs. Other broader role
assignments still apply; review them separately. Contributor is also powerful.
Protect `main` and restrict who can edit workflows.

#### Check registry access for image digests

The GitHub deployment identity must read registry image metadata to record and
verify digests. Contributor alone is not registry data-plane access. In the
prototype registry, open **Access control (IAM) → Role assignments** and
check the GitHub deployment service principal has `AcrPull` or `AcrPush` at
the registry scope or inherited from a suitable scope. If it has neither,
ask an Azure role administrator to grant **AcrPull** at this registry only.
Wait for role propagation before retrying. The automated create workflow
grants the deployment principal `AcrPush` on the registry; the interactive
quickstart may instead have granted `AcrPush` to the signed-in user. Do not
assume those are the same principal. Repeat this check if the registry is
deleted and recreated.

#### Add the notification worker identities and permissions

The separate worker needs two more retained identities. The worker identity
receives messages; a scaler-only identity reads queue metrics. The scaler uses
namespace-scoped **Azure Service Bus Data Owner**, a powerful role that can
manage entities and send and receive messages. The container cannot use that
scaler identity. This follows Microsoft's
[KEDA managed-identity example](https://learn.microsoft.com/azure/aks/keda-workload-identity#create-role-assignments);
the new setup still needs live Azure validation.

1. Open a Bash or zsh terminal at the root of this checked-out repository.
   You need Azure CLI and `jq`; do not use the GitHub service principal for
   this administrator step. Sign in as an Azure role administrator, replace
   the placeholder with your subscription ID, and check the result:

   ```bash
   az login
   az account set --subscription "<subscription-id>"
   az account show --output table
   jq --version
   ```

   Confirm the displayed subscription and tenant are correct before continuing.
2. Find the deployment application's **client ID** in its Entra app registration,
   or in GitHub → **Settings → Secrets and variables → Actions → Variables →
   AZURE_CLIENT_ID** if already configured. It is not a secret or the staff
   app ID. Replace both placeholders and run:

   ```bash
   export AZURE_RESOURCE_GROUP="<prototype-resource-group>"
   export AZURE_CLIENT_ID="<github-deployment-client-id>"
   bash infra/setup-notification-permissions.sh
   ```

3. Check the printed subscription group, GitHub principal and permission
   summary. Type `yes` only if they are correct. The script creates the two
   identities if missing and extends the existing conditioned group-scoped
   administrator assignment. It permits **receiver only for the worker** and
   **Data Owner only for the scaler**, for both assignment and removal; it
   preserves the existing condition. It refuses an unconditioned or ambiguous
   existing assignment. If it fails, read the error and review Azure before
   retrying.
4. The script reports when the extension is already present or when the
   restricted permissions are ready. In Azure Portal → resource group → **Resources**, confirm
   `mortgage-demo-env-notifications` and `mortgage-demo-env-notifications-scaler`
   now exist along with the original six. In **Access control (IAM) → Role
   assignments**, inspect the updated condition and check for other broader
   assignments. Allow time for permission propagation.

The scaler's Data Owner permission is restricted to the prototype namespace
when deployed, but the GitHub delegation condition allows assigning that role
to the scaler identity anywhere in this dedicated group. Contributor access and
other pre-existing permissions are unchanged.

### 3. Add Microsoft Graph permissions and grant consent

**Why:** The deployment app reads app registrations and updates its owned Staff
Web registration's callback URLs. Adding permissions and granting consent
are separate steps.

1. In **Microsoft Entra ID → App registrations**, open the **GitHub deployment
   app**, not Staff Web or Mortgage API. Confirm its **Application (client)
   ID** matches the GitHub `AZURE_CLIENT_ID` variable.
2. Open **API permissions → Add a permission → Microsoft Graph →
   Application permissions**. Add `Application.Read.All` and
   `Application.ReadWrite.OwnedBy`. Do not select delegated permissions.
3. Before granting consent, review the deployment principal's owned
   applications using the [owned-objects check](#4-make-the-deployment-principal-an-owner-of-staff-web).
   Also review the *whole* permissions list, including any other permissions
   awaiting consent. An appropriately authorized Entra administrator selects **Grant admin
   consent for <directory>** and confirms only if the permissions are
   understood and intended. This button may grant other listed permissions too.
4. Refresh the page. Both permissions must show **Granted for <directory>**;
   merely appearing in the list or showing **Not granted** is not enough.
   `Application.ReadWrite.OwnedBy` permits full management of every
   application this principal owns, not only its callback URLs.

To independently check the application grants, find the GitHub deployment
app under **Enterprise applications → All applications → <deployment app> →
Overview** and copy its **Object ID**. This is the service-principal object ID,
not the app registration's object ID or the client ID. In Azure Cloud Shell
(Bash), replace the placeholder and run this read-only command:

```bash
az rest --method get --url 'https://graph.microsoft.com/v1.0/servicePrincipals/<github-service-principal-object-id>/appRoleAssignments'
```

In the `value` array, confirm Microsoft Graph assignments include both
`appRoleId` values `9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30` and
`18a4783c-866b-4cc7-a460-3d5e5662c884`. An empty list means the application
grants are not present on that service principal. If results contain
`@odata.nextLink`, follow the next page rather than assuming a missing grant.
Do not share the full response or authentication details.

### 4. Make the deployment principal an owner of Staff Web

1. In **App registrations → Staff Web → Overview**, copy the registration's
   **Object ID** (not its client ID). For the owner, find the GitHub app under
   **Enterprise applications → All applications → <deployment app> →
   Overview** and copy its **Object ID**. This is the deployment service
   principal object ID, not its app registration object ID.
2. Open **Owners** and check whether that service principal is already an
   owner. Do not add a duplicate. If the portal cannot select service
   principals, an authorized Entra administrator can run the following in
   Cloud Shell (Bash), substituting both IDs:

   ```bash
   az rest --method post --url 'https://graph.microsoft.com/v1.0/applications/<staff-registration-object-id>/owners/$ref' --body '{"@odata.id":"https://graph.microsoft.com/v1.0/directoryObjects/<github-service-principal-object-id>"}'
   ```

3. Refresh **Owners** or run `az ad app owner list --id "<staff-web-client-id>"`
   and confirm the deployment service principal is listed. Do not remove
   existing owners without reviewing them.
4. Review the deployment service principal's owned applications before
   granting or relying on `Application.ReadWrite.OwnedBy`. The read-only query
   below lists owned directory objects; follow `@odata.nextLink` if present:

   ```bash
   az rest --method get --url 'https://graph.microsoft.com/v1.0/servicePrincipals/<github-service-principal-object-id>/ownedObjects'
   ```

   Confirm every owned application is intended. Investigate unexpected
   ownership with an administrator; do not remove it blindly.

### 5. Store GitHub repository variables and secrets

1. In GitHub, open the repository → **Settings → Secrets and variables →
   Actions → Variables**. Under **Repository variables**, select **New
   repository variable**. Add each name exactly as written. If it already
   exists, edit and verify it instead of creating another.

   | Variable | Where to find its value |
   | --- | --- |
   | `AZURE_CLIENT_ID` | GitHub deployment app registration → Overview → Application (client) ID |
   | `AZURE_TENANT_ID` | That registration → Overview → Directory (tenant) ID |
   | `AZURE_SUBSCRIPTION_ID` | Azure Portal → Subscriptions → the selected subscription → Subscription ID |
   | `AZURE_RESOURCE_GROUP` | The dedicated resource group's name |
   | `AZURE_CONTAINER_REGISTRY` | Resource group → container registry → name, without `.azurecr.io` |
   | `AZURE_SQL_SERVER` | Resource group → SQL server → name |
   | `AZURE_STORAGE_ACCOUNT` | Resource group → storage account → name |
   | `MORTGAGE_API_CLIENT_ID` | Mortgage API app registration → Application (client) ID |
   | `STAFF_CLIENT_ID` | Staff Web app registration → Application (client) ID |
   | `AZURE_ALERT_EMAIL` | Optional alert recipient; enabling the alert can add cost |
   | `OTLP_ENDPOINT` | Optional trusted OTLP collector endpoint |
   | `OTLP_PROTOCOL` | Optional `grpc` (default) or `http/protobuf` |

2. On the same GitHub Actions settings page, select **Secrets → New repository
   secret** and add `STAFF_CLIENT_SECRET`, `PRODUCTS_API_KEY`,
   `ELIGIBILITY_API_KEY` and `DEPLOYMENT_FINGERPRINT_KEY`. These are **secrets**, not variables. Use the Staff
   Web secret **value** and the two different service keys used by the
   quickstart. Keep them in a password manager; GitHub does not display saved
   secret values.
3. If creating fresh keys for a disposable deployment, run
   `openssl rand -hex 32` twice in a secure terminal and save the two
   different outputs separately. Each key must be 32–128 letters, digits, underscores or
   hyphens. For an existing deployment, reuse its matching keys: changing
   only one side can break service calls. Run `openssl rand -hex 32` once
   more for `DEPLOYMENT_FINGERPRINT_KEY`, save the output securely, and add
   it as that repository secret. This separate key protects fingerprints used
   to detect secret changes; keep it stable between runs.
4. Verify that the deployment app's federated credential matches the repository
   and `main` branch; see [create the GitHub deployment identity](azure-setup-from-scratch.md#6-create-the-github-deployment-identity).
   Restrict who can change trusted workflows and run them. Rotate the staff
   secret before expiry and update GitHub; rotation can interrupt the app.

Image builds and manual release and infrastructure workflows use only the first
five variables above.
The create and reconcile workflows also need the other four required variables
and four secrets. The final three variables are optional. All must be
**repository** settings, not environment-only settings.

