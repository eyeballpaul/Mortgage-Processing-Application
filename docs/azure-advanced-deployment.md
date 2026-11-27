# Advanced Azure deployment and operations

Use this guide for the interactive quickstart, notification operations, and
deployment of application changes. For first-time setup, start with the
[setup-from-scratch guide](azure-setup-from-scratch.md). For routine
create/delete runs and recovery after group deletion, use the
[prototype lifecycle guide](../AZURE_PROTOTYPE_GUIDE.md).

## Before deploying

- Install Azure CLI with Container Apps commands, `jq`, `openssl` and `curl`.
  Sign in with `az login` and select the intended subscription using
  `az account set --subscription <subscription>`. To automate GitHub setup,
  also install GitHub CLI and sign in with `gh auth login --hostname github.com`
  using an account allowed to manage repository variables.
- The signed-in Azure user needs permission to create a resource group at the
  subscription scope if it does not exist, deploy resources and create role
  assignments in that group. The user also needs Entra permission to assign app
  roles and update the staff registration's authentication settings.
- Set up the Mortgage API and staff app registrations as described in
  [README.md](../README.md#configure-entra-for-staff-and-the-api), including admin
  consent and the staff role assignments.
- Generate two different keys with `openssl rand -hex 32`. Save them in a secure
  password manager and reuse them for retries; do not commit them. Have the staff
  app client secret value ready.
- Check the Azure pricing calculator and Cost Management for your subscription and
  region before deployment. A budget sends alerts; it does not stop charges.

The template requests the Azure SQL free offer for each database, with
`AutoPause` on exhaustion: once a database uses its monthly free allowance, it
becomes unavailable until the following month. Availability of the offer depends
on the subscription and its existing free databases. Running replicas and health
checks can consume the allowance even when nobody is using the apps. The registry,
private endpoints, logging, storage and other resources may still incur charges.
There is no verified pause/resume feature and no guaranteed £20 monthly cost.
In Azure Cost Management, filter Cost analysis to the resource group and group
by resource to identify actual charges; check the subscription scope for costs
outside the group. Five continuously running apps, two private endpoints and the
Basic registry are important costs to monitor.

## Submission notifications

The quickstart provisions a **public** Service Bus Basic namespace and one
`submission-notifications` queue. Basic does not offer a private endpoint.
SAS authentication is disabled. The API publishes with its sender identity;
the separate Notifications Worker receives with its own receiver identity and
records a simulation in its own SQL database. **No email is sent.** Messages
contain an event ID, application ID, type/version and timestamp.

The worker has no ingress and is configured to scale from zero to one replica.
Its separate scaler identity has **Azure Service Bus Data Owner** on the
namespace, following [Microsoft's KEDA example](https://learn.microsoft.com/azure/aks/keda-workload-identity#create-role-assignments).
That role can manage the namespace's entities and send and receive messages:
it is broader than receiver access. Container Apps sets that identity's
lifecycle to `None`, so application containers cannot use it. See
[managed identity lifecycle](https://learn.microsoft.com/azure/container-apps/managed-identity#configure-managed-identity-lifecycle).
The worker identity itself has only queue-scoped receive access. The exact
scale-to-zero behavior, including the permissions and isolation, has **not
yet been tested in Azure**.

An earlier API-hosted consumer was observed in Azure; this new separate
worker has not been validated there. Health checks do not test the flow.

### Check a submission notification

1. With the prototype running, submit a fictional application. The Mortgage
   API saves the submission first; its publisher checks for pending events
   roughly every 30 seconds. A cold start or SQL resume can take longer.
2. In Azure Portal, open **Resource groups → your group →
   notifications-worker → Log stream**. Choose a running replica if
   prompted and look for `Simulated notification recorded`. No email is sent.
   The worker is configured to scale to zero, so a replica or its live log
   stream may not be available after it becomes idle. If needed, submit
   another fictional application and check while it is running.
3. If there is no record, inspect Mortgage API and worker logs and the queue's
   active and dead-letter counts as described under
   [monitoring](#monitoring-retries-and-cost). Check Container Apps system
   logs for scaler authentication errors. Do not treat an empty live stream
   by itself as proof that processing failed or succeeded.

### Enable on an existing quickstart deployment

An older deployment can still have the consumer running inside `mortgage-api`.
The create and regular deployment scripts stop before upgrading such an app:
two consumers writing to different databases could record a duplicate.

1. Check whether the existing prototype contains data or queued messages you
   need. Do not delete it if you need that data. Arrange and test a separate
   backup and migration/cutover first; an in-place legacy cutover is not
   automated.
2. If the prototype is disposable and you accept permanent data loss, use
   [Delete when finished](../AZURE_PROTOTYPE_GUIDE.md#delete-when-finished).
   Confirm deletion succeeded and inspect costs. Make sure the updated
   deletion workflow and scripts are committed and pushed to `main` first.
   The push may trigger a regular deployment that fails while the legacy
   consumer is present; that failure does not perform the cutover.
3. Complete the updated [one-time setup](azure-on-demand-setup.md#one-time-setup),
   including the two new retained identities and the restricted role condition.
   Then run [Create when needed](../AZURE_PROTOTYPE_GUIDE.md#create-when-needed).
   If resources were already deleted, begin with step 3.
4. Submit fictional customer and staff applications and follow
   [Check a submission notification](#check-a-submission-notification).
   Check Service Bus active/dead-letter counts and the notification database.
   Confirm the worker scales back to zero in Azure after its cooldown and starts
   again for another message. Check Container Apps system logs for scaler
   authentication errors if it does not.

On subsequent pushes, the application workflow reconciles the queue and
permissions, runs bootstrap if its dependencies changed, and updates affected
images. It cannot create a missing worker or other core infrastructure; use
the create workflow. A successful submission can precede publishing, because
the outbox retries later. File and in-memory providers do not create outbox
events. Aspire enables the publisher and worker with its
[local emulator](../README.md#local-notification-processing) by default in
Development; it does not connect to the Azure queue.

### One-time GitHub deployment permission

Follow [one-time setup](azure-on-demand-setup.md#2-allow-the-deployment-identity-to-manage-azure-resources).
It preserves the existing group-scoped condition and adds only two
role/principal pairs: receiver for the worker and Data Owner for the scaler.
Contributor and any other role assignments remain powerful; inspect them
separately. No new Entra app registration, client secret or Service Bus key
is needed. Register `Microsoft.ServiceBus` if required by the subscription.
Role propagation can take several minutes.

### Monitoring, retries and cost

The publisher checks SQL approximately every 30 seconds per running replica;
a failed or interrupted claim becomes available after its five-minute lease.
Sending followed by a crash can produce duplicates. The SQL simulation record
deduplicates by event ID, but this is **at-least-once**, not exactly-once
delivery. Processing errors are retried by Service Bus and eventually
dead-lettered; invalid events are dead-lettered immediately. Active messages
expire after 14 days and are moved to the dead-letter queue. Dead-lettered
messages are not automatically deleted.

Inspect Mortgage API and worker error logs, unpublished `SubmissionOutbox` rows,
the queue's active/dead-letter counts, and `SimulatedNotifications` in the
private `notifications` database. In the Azure portal, use Service Bus Explorer with suitable Entra
data-plane permissions to peek at dead-lettered messages and their reasons.
After fixing the cause, resend a valid message with its original message ID;
resending leaves the original in the dead-letter queue, so explicitly remove it
only after checking the result. There is no automated dead-letter replay, alert,
outbox/inbox cleanup or retention policy in this prototype.

Basic has usage-based operation charges rather than the Standard tier's hourly
base charge. Microsoft retail pricing checked for UK South was approximately
£0.0376 per million operations; rates and exchange rates change, and polling,
retries and management operations also count. SQL polling, Container Apps,
logging and network usage can add cost. Check
[Service Bus pricing](https://azure.microsoft.com/pricing/details/service-bus/)
and actual Cost Management charges; the £20 budget is not a spending cap.

## Deploy

Use this interactive path for the first deployment or after deleting the
entire group. For the normal one-click cycle, use
[Run the workflows](../AZURE_PROTOTYPE_GUIDE.md#run-the-workflows) instead.

1. Complete [Before deploying](#before-deploying) and the registration steps in
   the [first-time guide](azure-setup-from-scratch.md). In a terminal at
   the repository root, sign in and check the subscription:

   ```bash
   az login
   az account set --subscription "<subscription-id>"
   az account show --output table
   ```

2. Verify the displayed subscription and tenant, then run:

   ```bash
   bash infra/quickstart-deploy.sh
   ```

3. Enter the resource group name and, if it is new, its region. For registry,
   SQL server and storage, either accept the suggested names for a new
   deployment or enter the original names for a retry. Record these names.
4. Enter the Mortgage API and Staff Web **client IDs**, the staff secret
   **value**, and the two different service keys. Secret prompts do not echo
   input; type or paste the value and press Enter.
5. At the GitHub setup prompt, enter `n` for manual setup if this is the first
   time and the existing deployment app, federated credential and GitHub
   variables are not ready. The default is **yes**. If choosing automatic
   setup, enter and check the exact `owner/repository` when prompted.
6. Read the subscription, resource names, callback and GitHub summary. Only
   if everything is correct, type `yes` to start. Existing app settings,
   images and secrets may be overwritten. Otherwise stop and correct the
   inputs. Wait for the process to finish and record the displayed URLs.
7. Open both URLs and test sign-in and a fictional application. If a command
   fails, read its error and inspect the resource group and costs before a
   retry. See [staff troubleshooting](#staff-sign-in-troubleshooting) and
   [workflow troubleshooting](../AZURE_PROTOTYPE_GUIDE.md#workflow-troubleshooting).

Confirm the subscription, resource group, resource names, staff registration
and GitHub repository carefully. If the group is missing, choose its region and
the script creates it only after confirmation. It deploys the foundation,
assigns the customer managed identity its API role, builds seven images, runs the
private database bootstrap, deploys the apps, adds the staff URL's `/signin-oidc`
and `/signout-callback-oidc` paths without removing existing redirect URIs, and
checks public readiness. Avoid changing the registration concurrently.

For automatic GitHub setup, the repository must already have the correct
`AZURE_CLIENT_ID` and `AZURE_TENANT_ID` repository variables and an existing
deployment app with the correct OIDC federated credential. The selected tenant
must match. The script grants the Azure permissions and, after readiness,
updates `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` and
`AZURE_CONTAINER_REGISTRY` in the confirmed repository. It does not create an
Entra app or federated credential. Its queue-scoped condition only covers
sender and receiver assignments for the Mortgage API identity; it is not
sufficient for the worker and scaler. Complete the
[group-scoped setup and notification extension](azure-on-demand-setup.md#2-allow-the-deployment-identity-to-manage-azure-resources)
for either GitHub deployment path. Choose no for manual GitHub setup if this
is a first-time setup or GitHub CLI is unavailable.

The script exits on failure; partial Azure resources and configuration changes
remain and can be billable. GitHub variable updates can also be partial. Correct
the error and retry with the same names and API keys. It detects an existing SQL
server and omits its creation-only administrator settings on subsequent runs.
Readiness checks do not verify sign-in, document uploads or a complete
submission. Test those separately with fictional data.

For retries, specify the same registry, SQL and storage names rather than accepting
new random defaults; otherwise additional billable resources can be created.
Reapplying the quickstart to an existing deployment also reapplies the full Bicep
template, including app images, settings and secrets. Supply the currently valid
staff secret and the matching API keys; it can overwrite changes made in Azure
Portal. Run it from the intended source revision and do not run it at the same
time as the GitHub workflow.

### Staff sign-in troubleshooting

- `AADSTS650053`: on the configured Mortgage API app registration, verify that
  the delegated scope has the exact value `Mortgage.Staff` and is enabled.
  Add that delegated permission to the Staff Web registration.
- Admin consent required: an authorized tenant administrator must grant consent
  for that permission on Staff Web's **API permissions** page.
- `AADSTS7000215`: check that the Staff Web secret is the secret **value** for
  the deployed Staff Web client ID, not its ID, and has not expired. If updating
  `staff-client-secret` in the Container App, restart the active revision or
  deploy a new revision to pick it up. Retain the current value for later
  quickstart deployments.
- Staff access denied: check that the signed-in user is assigned to the
  `MortgageStaff` role on both enterprise applications. Each registration's
  enabled role must have **Value** exactly `MortgageStaff` and allow
  Users/Groups; the display name alone is not sufficient. Sign in again after
  assignments change.

The staff Container App's console logs can help identify errors:

```bash
az containerapp logs show -g "<resource-group>" -n staff-web --type console --tail 100
```

Redact personal data, tokens, authorization codes and secrets before sharing logs.

## Deploy application changes with GitHub Actions

The existing GitHub Actions workflow builds and tests pull requests. After a
successful build on a push to `main`, it deploys changed application images to
an **existing compatible** quickstart deployment and applies the notification
Bicep and settings. It cannot create a missing worker or upgrade an older
API-hosted consumer; follow the
[legacy cutover](#enable-on-an-existing-quickstart-deployment). It does not
create the resource group, core infrastructure or Entra registrations, or
manage secrets. Review other infrastructure changes and deploy them separately.

1. In Entra, create a **separate** single-tenant app registration for deployment,
   with no redirect URI required. On the registration, open **Certificates &
   secrets → Federated credentials → Add credential**, and select **Other
   issuer**. Create a credential with:

   | Field | Value |
   | --- | --- |
   | Issuer | `https://token.actions.githubusercontent.com` |
   | Subject identifier | `repo:eyeballpaul@3922189/Mortgage-Processing-Application@1437634495:ref:refs/heads/main` |
   | Name | `github-main-immutable` |
   | Audience | `api://AzureADTokenExchange` |

   The subject above is the immutable subject emitted by this repository's
   `main` workflow. A fork, renamed or transferred repository, or customized
   GitHub OIDC subject may need a different value. Match the exact subject in
   the GitHub login log; see [GitHub's Azure OIDC guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure).
   Grant the deployment app's service principal **Contributor** on the existing
   resource group, plus the constrained role-assignment permission in
   [One-time GitHub deployment permission](#one-time-github-deployment-permission).
   The workflow uses OIDC and does not need a client secret.
   Protect `main` and restrict who can change the workflow: the federated
   identity can update the resources in that group.
2. In GitHub, under **Settings → Secrets and variables → Actions → Variables**,
   add **repository variables** `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
   `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` and
   `AZURE_CONTAINER_REGISTRY`. Use the deployment app's application (client)
   ID, its directory (tenant) ID and the existing resource names, not the staff
   app client ID. Use the registry name without `.azurecr.io`. Do not put secret
   values in these variables; this workflow does not use environment variables
   from a GitHub environment.
3. Commit and push the workflow and deployment scripts to `main`. The first run
   builds all seven images because quickstart images do not contain a Git commit
   baseline. Later runs compare each service's last successful deployed commit
   against the new commit, including transitive project references and linked
   files. If a baseline is unavailable, the service is rebuilt rather than
   silently skipped. A failed deployment must pass on a later retry before its
   new image is recorded as successful.

The customer project directory is now `PD.Mortgages.Customer.Web`, but its
Azure app and image are still `mortgages-web`. The solution-file change is
treated as a shared build change: a deployment comparing against a commit
before the rename rebuilds all six app images and the bootstrap image. These
builds can incur charges.

If the database bootstrap project or its dependencies change, the workflow
builds and runs the existing private Container Apps job before updating apps.
Migrations must remain compatible with the currently running revisions; use
expand-and-contract migrations. After an app update or bootstrap run, the pipeline checks public readiness and
API dependencies, not a full sign-in, upload or submission. A run with neither
an app update nor a bootstrap run exits without those checks. It stores successful
commit markers in resource tags and uses commit-tagged images. Azure Registry
builds, stored images, running apps and databases may incur charges. GitHub
Actions usage may also be billed by GitHub depending on repository visibility,
plan and usage; that billing is separate from an Azure budget.

### GitHub deployment troubleshooting

- If Azure login reports that `client-id` or `tenant-id` is missing, verify
  `AZURE_CLIENT_ID` and `AZURE_TENANT_ID` are populated as repository variables
  with those exact names. Also check `AZURE_SUBSCRIPTION_ID`.
- `AADSTS700213` means Azure found no matching federated credential. Check the
  issuer, audience and exact subject shown by the login step against the
  credential on the app identified by `AZURE_CLIENT_ID`. This repository uses
  the immutable subject above, not the older name-only subject.
- If deployment reports a legacy consumer or a missing `notifications-worker`,
  follow the [legacy cutover](#enable-on-an-existing-quickstart-deployment).
  Do not delete data you need; a data-preserving cutover is not automated.
- After correcting the configuration, select **Re-run jobs → Re-run failed
  jobs** on the GitHub Actions run. Readiness checks do not replace testing
  staff sign-in and a full journey with fictional data.

