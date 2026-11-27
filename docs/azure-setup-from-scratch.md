# Azure prototype: setup from scratch

This guide sets up the prototype in Azure and enables GitHub Actions to deploy
application changes. Replace values in `<angle-brackets>` with your own; do not
include the brackets. Do not put secret values in this file, Git, chat or logs.
Keep them in a password manager.

The quickstart and an initial GitHub deployment have succeeded in one Azure
environment. Creating every registration from scratch and a later selective
incremental deployment have not been independently validated. Use fictional
data only.

For the separate, unverified one-click create and delete workflows, complete
the [on-demand workflow setup](../AZURE_DEPLOYMENT.md#on-demand-github-workflows)
after the initial interactive deployment and GitHub identity setup below.

If you deleted only the resource group, the Entra registrations and GitHub
configuration are outside it. Reuse them rather than creating duplicates. The
quickstart can recreate the group, update staff callbacks and restore GitHub
permissions and variables; see [recreation](../AZURE_DEPLOYMENT.md#after-deleting-the-resource-group).
If you create new registrations, use their new IDs rather than old ones.

## 1. Prepare

1. Install Azure CLI with Container Apps commands, `jq`, `openssl` and `curl`.
   For automatic GitHub setup, also install GitHub CLI and sign in using
   `gh auth login --hostname github.com`. Check out this repository and open a
   terminal at its root.
2. Use an Entra user allowed to deploy resources, create Azure RBAC assignments
   and assign Entra app roles, and allowed to update the staff app registration.
   The user also needs permission to create a group in the subscription if it
   does not exist. An appropriate Entra administrator must grant API consent.
   GitHub repository administration access is needed to update variables.
3. Sign in, select your subscription and check it:

   ```bash
   az login
   az account set --subscription "<subscription-id>"
   az account show --output table
   ```

4. Review the [Azure deployment guide](../AZURE_DEPLOYMENT.md) and pricing for
   your region. Create a monthly budget alert in Azure Cost Management. A budget
   does not cap spending. Running apps, private endpoints, the registry, builds,
   storage and logs can incur charges even when the prototype is idle.

## 2. Create the Mortgage API registration

1. Azure Portal → **Microsoft Entra ID → App registrations → New registration**.
   Name it `Mortgage API`, select **Accounts in this organizational directory
   only**, leave the redirect URI blank and select **Register**.
2. On **Overview**, copy its **Application (client) ID** for the quickstart.
   This is different from the registration's object ID.
3. Open **Expose an API** and set the Application ID URI to
   `api://<mortgage-api-client-id>`. Add an enabled scope named exactly
   `Mortgage.Staff`, select **Admins only**, provide a consent display name and
   description, and save it.
4. Open **App roles → Create app role** and add these enabled roles:

   | Display name | Allowed member types | Value |
   | --- | --- | --- |
   | `MortgageCustomer` | Applications | `MortgageCustomer` |
   | `MortgageStaff` | Users/Groups | `MortgageStaff` |

   Add a description to each. The **Value** must match exactly; Entra generates
   new role IDs for a new registration.
5. The [README](../README.md#configure-entra-for-staff-and-the-api) recommends
   setting `api.requestedAccessTokenVersion` to `2` in the manifest. The
   previously working registration was observed with `null`, so changing that
   setting on an existing registration is not a verified reproduction of its
   configuration. Test customer and staff API access before relying on a new
   or changed setting.
6. Confirm that an enterprise application exists for the registration. The
   quickstart assigns the customer managed identity the `MortgageCustomer` role.

## 3. Create Staff Web and configure access

1. Under **App registrations → New registration**, create a single-tenant app
   named `Staff Web`. Copy its **Application (client) ID**. The quickstart adds
   the Azure callback URLs.
2. Under **App roles**, create an enabled role with display name and **Value**
   exactly `MortgageStaff`, allowed member types **Users/Groups**, and a
   description.
3. Under **Certificates & secrets → Client secrets → New client secret**, create
   a secret. Immediately save its **Value** in a password manager and record
   its expiry. The secret ID is not the value, and the value cannot be viewed
   again after creation.
4. Under **API permissions → Add a permission → My APIs**, choose Mortgage API,
   then **Delegated permissions → Mortgage.Staff → Add permissions**. An
   authorized administrator must select **Grant admin consent** and confirm the
   permission is granted.
5. Under **Enterprise applications**, open **Staff Web → Users and groups → Add
   user/group**. Select the staff user and `MortgageStaff` role and assign it.
   Repeat for the **Mortgage API** enterprise application and its
   `MortgageStaff` role. Both role values must be `MortgageStaff` and allow
   users; do not assign the Applications-only `MortgageCustomer` role to a user.
   An administrator may need to make the assignments.
6. If reusing registrations, verify the scope, consent and both assignments
   rather than creating duplicates. The quickstart assigns the separate
   `MortgageCustomer` role to the new customer managed identity.

## 4. Create the resource group and deploy

1. Choose a resource group name and region. The quickstart creates a missing
   group after confirmation. If the group already exists, inspect it first; do
   not overwrite unrelated resources.
2. Generate **two different** service API keys by running this command twice.
   Store each result separately in a password manager as the Products key and
   Eligibility key. Do not paste the outputs into Git, chat or logs.

   ```bash
   openssl rand -hex 32
   ```

   For retries against an existing deployment, reuse its current two keys and
   current staff secret; mismatched secrets can break calls between apps.
3. From the repository root, start the interactive deployment:

   ```bash
   bash infra/quickstart-deploy.sh
   ```

4. Enter the resource group (and region if new), globally available registry name, SQL
   server name and storage account name. Supply the Mortgage API and Staff Web
   **client IDs**, the staff secret **value**, and the two service keys when
   prompted. Secret prompts are hidden. Check the subscription, group and names
   before confirming. Record the resource names securely for retries. On the
   first setup, choose **no** for automatic GitHub configuration unless the
   deployment app, its OIDC credential and the `AZURE_CLIENT_ID` and
   `AZURE_TENANT_ID` repository variables already exist. Complete sections 6–7
   manually otherwise. On recreation, choose **yes** and confirm the repository.
5. The script deploys the infrastructure, including a public Service Bus Basic
   namespace and queue, assigns the customer API role, builds
   six images, runs the private database bootstrap job and deploys five apps.
   Record the customer and staff URLs printed at the end. On failure, inspect
   the error; partial resources remain billable. A retry reapplies the template
   and can overwrite app settings, images and secrets. Use the intended source
   revision, existing names and matching secrets.
   The notification flow records a simulation only; it sends no email. The new
   customer notification simulation has been observed in Azure. See
   [notification setup and operations](../AZURE_DEPLOYMENT.md#submission-notifications).

## 5. Finish Staff Web setup and test

1. The quickstart adds these **Web** redirect URIs to the staff registration
   while retaining existing redirects, replacing the placeholder with the
   deployed HTTPS staff URL:

   ```text
   <staff-url>/signin-oidc
   <staff-url>/signout-callback-oidc
   ```

   Check **App registrations → Staff Web → Authentication** if sign-in fails.
2. Open the customer and staff URLs. Sign in to Staff Web with the assigned
   account and exercise a customer-to-staff journey with fictional data.
   Readiness checks alone do not verify sign-in, document upload or submission.
   If access is denied, check the tenant, consent, exact role values and both
   enterprise-app assignments.

## 6. Create the GitHub deployment identity

If reusing an existing deployment app and choosing automatic GitHub setup in
the quickstart, verify its OIDC credential instead of creating a duplicate;
the script restores its permissions. Otherwise follow these manual steps.

1. Create a separate single-tenant Entra app registration for GitHub deployment,
   with no redirect URI. Copy its **Application (client) ID** and **Directory
   (tenant) ID**. Do not use the Staff Web or Mortgage API client ID.
2. On the deployment app, open **Certificates & secrets → Federated
   credentials → Add credential → Other issuer**. Enter:

   | Field | Value |
   | --- | --- |
   | Issuer | `https://token.actions.githubusercontent.com` |
   | Subject identifier | The exact GitHub OIDC subject for your repository's `main` branch |
   | Name | A descriptive name, such as `github-main-immutable` |
   | Audience | `api://AzureADTokenExchange` |

   For repositories using GitHub's immutable subject format, the subject is:

   ```text
   repo:<owner>@<owner-id>/<repository>@<repository-id>:ref:refs/heads/main
   ```

   Obtain the owner and repository IDs from GitHub's OIDC settings or API.
   Older repositories can use a name-only subject, and customized subjects may
   differ. Check the exact subject in the GitHub login log if login fails; see
   [GitHub's Azure OIDC guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure).
   No deployment client secret is needed.
3. Azure Portal → **Resource groups → your group → Access control (IAM) → Add
   → Add role assignment**. Choose **Contributor**, then **User, group, or
   service principal**. Select the GitHub deployment app and choose **Review +
   assign**. The Entra registration itself remains after group deletion.
4. An Azure administrator must also grant the GitHub deployment identity the
   constrained role-assignment permission in the [deployment guide](../AZURE_DEPLOYMENT.md#one-time-github-deployment-permission).
   When using manual setup after recreation, use the new Mortgage API managed
   identity; automatic setup handles this instead.

## 7. Configure and run GitHub Actions

1. GitHub repository → **Settings → Secrets and variables → Actions →
   Variables**. Under **Repository variables**, add:

   | Name | Value |
   | --- | --- |
   | `AZURE_CLIENT_ID` | GitHub deployment app's application client ID |
   | `AZURE_TENANT_ID` | Deployment app's directory tenant ID |
   | `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
   | `AZURE_RESOURCE_GROUP` | Existing resource group name |
   | `AZURE_CONTAINER_REGISTRY` | Existing registry name, without `.azurecr.io` |

   These are identifiers, not secret values. The workflow does not select a
   GitHub environment, so environment-only variables are not available to it.
2. Protect `main` and restrict workflow-editing access: the deployment identity
   has Contributor access to the resource group and constrained role-assignment
   access to the notification queue (or the group when set up manually).
3. Push or merge an application change to `main`. After build and tests pass,
   the workflow deploys to the **existing** resources. On the first deployment
   after quickstart, no commit baselines exist, so it builds all six images and
   runs bootstrap. Later runs select affected projects; this selective path still
   needs a live test. The workflow applies the Service Bus notification Bicep and
   settings, but does not create the core infrastructure or update secrets.
   Do not run it at the same time as the manual quickstart.
4. Watch **Actions → Build, test and security checks → deploy-existing**.
   Verify readiness, staff sign-in and the application journey separately.
   Builds and Azure resources can incur charges.
5. If login reports a missing client or tenant ID, check the exact repository
   variable names and values. For `AADSTS700213`, compare the login log's
   issuer, audience and subject with the federated credential on the app whose
   client ID is in `AZURE_CLIENT_ID`. After correcting settings, use **Re-run
   jobs → Re-run failed jobs** on the appropriate current-commit run.

## 8. When finished

If data is disposable, delete the intended resource group in Azure Portal. This
permanently deletes its databases, blobs, identities and other resources.
Verify deletion and check for resources and charges outside the group. Entra
registrations and subscription-level budgets remain. There is no verified
whole-environment pause; do not rely on the older pause script to stop charges.

See the [Azure deployment guide](../AZURE_DEPLOYMENT.md) for retry, recreation,
cost and troubleshooting details.
