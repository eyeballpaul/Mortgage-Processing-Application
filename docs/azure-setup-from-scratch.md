# Azure prototype: setup from scratch

This guide sets up the prototype in Azure and enables GitHub Actions to deploy
application changes. Replace values in `<angle-brackets>` with your own; do not
include the brackets. Do not put secret values in this file, Git, chat or logs.
Keep them in a password manager.

An earlier quickstart and initial GitHub deployment succeeded in one Azure
environment. Creating every registration from scratch, the separate worker
and its scale rule, and a later selective incremental deployment have not been
independently validated. Use fictional data only.

For the separate one-click create and delete workflows, complete
the [on-demand workflow setup](azure-on-demand-setup.md)
after the initial interactive deployment and GitHub identity setup below.
Earlier versions were exercised in Azure; deletion needed a retry. The updated
workflows, separate worker, scale rule and DNS-link ordering fix have not yet
been tested live.

## What you are going to do

1. Prepare an Azure subscription, a GitHub repository and a terminal.
2. Create two Entra registrations: one identifies the Mortgage API and one
   allows staff to sign in. An **app registration** describes an app;
   its **enterprise application** is the corresponding identity in your
   tenant. Their object IDs differ.
3. Run the interactive quickstart once. It creates billable Azure resources
   and eight managed identities needed for later runs.
4. Create a separate GitHub deployment identity and configure access.
5. Test the apps, then set up the on-demand buttons. When you are finished,
   delete the prototype resources while keeping the group and identities.

You need an Azure subscription, access to the Azure tenant, a copy of this
repository on GitHub, and permission to administer its settings. You may need
an Azure role administrator and an Entra administrator to complete specific
steps. Stop and ask them for help if a permission or consent button is
unavailable; do not broaden permissions just to get past an error.

If you deleted only the resource group, the Entra registrations and GitHub
configuration are outside it. Reuse them rather than creating duplicates. The
quickstart can recreate the group, update staff callbacks and restore GitHub
permissions and variables; see [recreation](../AZURE_PROTOTYPE_GUIDE.md#after-deleting-the-resource-group).
If you create new registrations, use their new IDs rather than old ones.

## 1. Prepare

1. Install [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli),
   [`jq`](https://jqlang.org/download/), OpenSSL and curl. For optional
   automatic GitHub setup, install [GitHub CLI](https://cli.github.com/) too.
   The Container Apps commands must be available in Azure CLI.
2. Clone or open this repository on your computer. In VS Code, choose
   **Terminal → New Terminal**. The terminal must be at the repository root:
   it is the folder containing `infra` and `README.md`. Run `pwd` and `ls`
   to check.
3. Check the tools, one command at a time:

   ```bash
   az version
   az containerapp job execution show --help
   jq --version
   openssl version
   curl --version
   ```

   If a command is missing, install or update that tool before continuing.
   The quickstart stops if the Container Apps commands are unavailable.
   If using automatic GitHub setup, run `gh auth login --hostname github.com`
   and follow its prompts with a GitHub account allowed to manage repository
   variables.
4. Use an Entra user allowed to deploy resources, create Azure RBAC assignments
   and assign Entra app roles, and allowed to update the staff app registration.
   The user also needs permission to create a group in the subscription if it
   does not exist. An appropriate Entra administrator must grant API consent.
   GitHub repository administration access is needed to update variables.
5. Sign in, select your subscription and check it. Replace the placeholder with
   the **subscription ID** from Azure Portal → **Subscriptions**, not an app
   or tenant ID:

   ```bash
   az login
   az account set --subscription "<subscription-id>"
   az account show --output table
   ```

   Confirm `az account show` displays the expected subscription and tenant.
   Stop if it does not.
6. In Azure Portal, open **Cost Management + Billing**, select your
   subscription, then **Budgets → Add**. Set a monthly amount and alert
   recipients appropriate for you, review and create it. Check the
   [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/)
   for your region. A budget sends alerts; it **does not cap spending**.
   Running apps, private endpoints, the registry, builds, storage and logs can
   incur charges even when the prototype is idle. No whole-environment pause
   is verified, and the total is not guaranteed to stay below £20.

## 2. Create the Mortgage API registration

1. Open [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID →
   App registrations → New registration**. Name it `Mortgage API`, select
   **Accounts in this organizational directory only**, leave the redirect URI
   blank and select **Register**.
2. On **Overview**, copy its **Application (client) ID** for the quickstart.
   This is different from the registration's object ID.
3. Open **Expose an API → Add a scope**. When prompted, set the Application
   ID URI to `api://<mortgage-api-client-id>`, using the client ID from
   step 2. For **Scope name** enter `Mortgage.Staff`; choose **Admins only**,
   enter a meaningful consent display name and description, set it to
   **Enabled**, and select **Add scope**. The staff app requests this
   delegated permission to call the API on a signed-in user's behalf.
4. Open **App roles → Create app role** and add these enabled roles:

   | Display name | Allowed member types | Value |
   | --- | --- | --- |
   | `MortgageCustomer` | Applications | `MortgageCustomer` |
   | `MortgageStaff` | Users/Groups | `MortgageStaff` |

   For each row select **Create app role**, enter the display name, member
   type, value and a description, ensure **Enable this app role** is selected,
   then **Apply**. The **Value** must match exactly; the display name alone
   is not enough. Entra generates new role IDs for a new registration.
5. For a **new** registration, open **Manifest**, find
   `api.requestedAccessTokenVersion`, set it to `2`, and save. This requests
   v2 API access tokens. The previously working Azure registration was
   observed with `null`: do not change an existing working registration
   without planning and testing the change. Test both customer and staff
   API access before relying on a new or changed setting.
6. Go to **Microsoft Entra ID → Enterprise applications → All applications**,
   search for `Mortgage API`, and confirm it is present in the correct tenant.
   The quickstart assigns the customer managed identity the `MortgageCustomer`
   role on this enterprise application.

## 3. Create Staff Web and configure access

1. Go back to **App registrations → New registration**. Enter `Staff Web`,
   select **Accounts in this organizational directory only**, leave the
   redirect URI blank for now, and select **Register**. On **Overview**, copy
   its **Application (client) ID**. The quickstart adds the Azure callback
   URLs after the app is deployed.
2. Under **App roles**, create an enabled role with display name and **Value**
   exactly `MortgageStaff`, allowed member types **Users/Groups**, and a
   description.
3. Under **Certificates & secrets → Client secrets → New client secret**, add
   a description and choose an expiry that fits your policy. Select **Add**.
   Immediately save its **Value** in a password manager and record its expiry.
   The secret ID is not the value, and the value cannot be viewed again after
   leaving the page. Never paste the value into chat, Git, screenshots or a
   GitHub variable.
4. Under **API permissions → Add a permission → My APIs**, choose Mortgage API,
   then **Delegated permissions → Mortgage.Staff → Add permissions**. An
   authorized administrator must review all listed permissions, select
   **Grant admin consent for <directory>**, confirm, and verify the status
   says **Granted**. Adding the permission alone is not consent.
5. Under **Enterprise applications**, open **Staff Web → Users and groups → Add
   user/group**. Select the staff user and `MortgageStaff` role and assign it.
   Repeat for the **Mortgage API** enterprise application and its
   `MortgageStaff` role. Both role values must be `MortgageStaff` and allow
   users; do not assign the Applications-only `MortgageCustomer` role to a user.
   Choose **Assign** and verify the user and role appear in the list on each
   enterprise application. An administrator may need to make the assignments.
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
   first setup, enter **n** for automatic GitHub configuration unless the
   deployment app, its OIDC credential and the `AZURE_CLIENT_ID` and
   `AZURE_TENANT_ID` repository variables already exist. Complete sections 6–7
   manually otherwise. The default is **yes**, so do not simply press Enter
   if you need manual setup. On recreation with the prerequisites in place,
   choose **yes** and confirm the repository.
5. The script deploys the infrastructure, including a public Service Bus Basic
   namespace and queue, assigns the customer API role, builds
   seven images, runs the private database bootstrap job and deploys six apps.
   Record the customer and staff URLs printed at the end. On failure, inspect
   the error; partial resources remain billable. A retry reapplies the template
   and can overwrite app settings, images and secrets. Use the intended source
   revision, existing names and matching secrets.
   The notification flow records a simulation only; it sends no email. An
   earlier API-hosted simulation was observed in Azure; the separate worker
   has not yet been tested live. See
   [notification setup and operations](azure-advanced-deployment.md#submission-notifications).

## 5. Finish Staff Web setup and test

1. The quickstart adds these **Web** redirect URIs to the staff registration
   while retaining existing redirects, replacing the placeholder with the
   deployed HTTPS staff URL:

   ```text
   <staff-url>/signin-oidc
   <staff-url>/signout-callback-oidc
   ```

   Check **App registrations → Staff Web → Authentication** if sign-in fails.
2. Open the customer URL in a browser and confirm the page loads. Open the
   staff URL and sign in with the assigned staff account.
3. Submit an application using fictional data and check that it appears in
   Staff Web. For the notification check, follow the
   [worker verification steps](azure-advanced-deployment.md#check-a-submission-notification);
   no email is sent. The worker and its scale rule still need live validation.
   Readiness checks alone do not verify sign-in, document upload or submission.
   If access is denied, check the tenant, consent, exact role values and both
   enterprise-app assignments.

## 6. Create the GitHub deployment identity

If reusing an existing deployment app and choosing automatic GitHub setup in
the quickstart, verify its OIDC credential instead of creating a duplicate;
the script restores Contributor and a queue-scoped permission for the Mortgage
API identity. Both GitHub deployment paths still need the group-scoped
condition and notification extension below.
Otherwise follow these manual steps.

1. Open **Microsoft Entra ID → App registrations → New registration**. Create
   a separate app such as `GitHub deployment`; select **Accounts in this
   organizational directory only**, leave the redirect URI blank, and select
   **Register**. On **Overview**, copy its **Application (client) ID** and
   **Directory (tenant) ID**. Do not use the Staff Web or Mortgage API client
   ID. This app identifies GitHub to Azure without a deployment client secret.
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

   For this repository, the observed subject is:

   ```text
   repo:eyeballpaul@3922189/Mortgage-Processing-Application@1437634495:ref:refs/heads/main
   ```

   Do not copy that value for a fork, renamed or transferred repository.
   To look up your own numeric IDs, sign in with `gh auth login --hostname
   github.com`, then replace the placeholders and run:

   ```bash
   gh api users/<owner> --jq .id
   gh api repos/<owner>/<repository> --jq .id
   ```

   These IDs help construct the immutable subject, but check whether your
   repository actually uses that format before saving the credential.
   Older repositories can use a name-only subject, and customized subjects may
   differ. Check the exact subject in the GitHub login log if login fails; see
   [GitHub's Azure OIDC guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure).
   No deployment client secret is needed.
3. Azure Portal → **Resource groups → your group → Access control (IAM) → Add
   → Add role assignment**. Choose **Contributor**, then **User, group, or
   service principal**. Select the GitHub deployment app and choose **Review +
   assign**. The Entra registration itself remains after group deletion.
4. An Azure administrator must also grant constrained role-assignment
   permissions. Follow the
   [one-time setup](azure-on-demand-setup.md#2-allow-the-deployment-identity-to-manage-azure-resources)
   for the initial five-role, five-principal **group-scoped** condition and
   its notification extension. This is the documented setup for both the
   on-demand workflows and the regular application deployment. The
   quickstart's optional queue-scoped condition alone is insufficient for
   the separate worker and scaler.

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
   access through a conditioned group-scoped role assignment.
3. To enable the on-demand workflows, complete
   [one-time setup](azure-on-demand-setup.md#one-time-setup), including the
   additional repository variables, secrets, Graph consent and Staff Web
   ownership. Return here when those checks are complete.
4. Push or merge an application change to `main`. After build and tests pass,
   the workflow deploys to the **existing compatible** resources. It cannot
   create a missing worker or upgrade a legacy API-hosted consumer. If an
   older prototype is still running, follow the
   [legacy cutover](azure-advanced-deployment.md#enable-on-an-existing-quickstart-deployment)
   before expecting the deployment to succeed. On the first deployment
   after quickstart, no commit baselines exist, so it builds all seven images and
   runs bootstrap. Later runs select affected projects; this selective path still
   needs a live test. The workflow applies the Service Bus notification Bicep and
   settings, but does not create the core infrastructure or update secrets.
   Do not run it at the same time as the manual quickstart.
5. Watch **Actions → Build, test and security checks → deploy-existing**.
   Verify readiness, staff sign-in and the application journey separately.
   Builds and Azure resources can incur charges.
6. If login reports a missing client or tenant ID, check the exact repository
   variable names and values. For `AADSTS700213`, compare the login log's
   issuer, audience and subject with the federated credential on the app whose
   client ID is in `AZURE_CLIENT_ID`. After correcting settings, use **Re-run
   jobs → Re-run failed jobs** on the appropriate current-commit run.

## 8. When finished

Use **Actions → Delete prototype resources** as described in
[delete when finished](../AZURE_PROTOTYPE_GUIDE.md#delete-when-finished). This
destroys prototype data but keeps the dedicated group and eight managed
identities so the create workflow can be used again. Verify success and review
costs.

Deleting the whole resource group is a separate, more disruptive alternative:
it also removes identities and group permissions, so the create button alone
cannot recreate the prototype. See
[resource-group recreation](../AZURE_PROTOTYPE_GUIDE.md#after-deleting-the-resource-group).
There is no verified whole-environment pause; do not rely on the older pause
script to stop charges.

See the [Azure deployment guide](../AZURE_PROTOTYPE_GUIDE.md) for retry, recreation,
cost and troubleshooting details.
