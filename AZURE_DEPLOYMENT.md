# Azure prototype deployment

The quickstart deploys two public web apps, three internal APIs, three SQL databases,
and private SQL and Blob endpoints in one VNet. This is a prototype: use fictional
data only. The quickstart, staff sign-in and an initial GitHub Actions deployment
have succeeded in one Azure environment. The workflow's readiness and dependency
checks passed, but a subsequent selective incremental deployment and a complete
customer-to-staff journey have not yet been validated.

For detailed first-time setup, see the [setup-from-scratch guide](docs/azure-setup-from-scratch.md).

## Before deploying

- Install Azure CLI with Container Apps commands, `jq`, `openssl` and `curl`.
  Sign in with `az login` and select the intended subscription using
  `az account set --subscription <subscription>`.
- Create an empty resource group in the chosen region. The signed-in Entra user
  needs permission to deploy resources and create Azure RBAC role assignments in
  that group, plus permission to assign app roles in Entra.
- Set up the Mortgage API and staff app registrations as described in
  [README.md](README.md#configure-entra-for-staff-and-the-api), including admin
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

The quickstart now provisions a **public** Service Bus Basic namespace and one
`submission-notifications` queue. Basic does not offer a private endpoint. The
Mortgage API managed identity has queue-scoped sender and receiver roles; SAS
authentication is disabled. The API hosts both workers, avoiding another
always-running Container App but sharing its identity and compute. Queue
messages contain only an event ID, application ID, type/version and timestamp.
The consumer writes a simulated notification to SQL: **no email is sent**.

The namespace, queue, role assignments and new flow have not yet been deployed
or tested end to end in Azure. Health checks do not test them. Use fictional
data and verify both customer and staff submissions.

### Enable on an existing quickstart deployment

Before pushing these changes, grant the existing GitHub deployment identity
the additional, constrained role-assignment permission described below.
No new Entra app registration, client secret or Service Bus key is needed.
Also check **Subscriptions → your subscription → Resource providers** for
`Microsoft.ServiceBus`. If it is not registered, a subscription administrator
with resource-provider registration permission must register it.

On each successful push to `main`, the deployment workflow runs the database
bootstrap if needed, applies [notifications.bicep](infra/notifications.bicep),
then updates the Mortgage API image and notification settings. It creates or
reconciles the namespace, queue and queue-scoped roles, even when no images
have changed. It expects the quickstart identity `mortgage-demo-env-mortgages`
to be attached to `mortgage-api` and selected by `AZURE_CLIENT_ID`. The
workflow fails rather than enabling notifications with a different identity.
It preserves unrelated app settings and secrets, and skips the app update if
the image and notification settings already match.

After deployment, submit fictional customer and staff applications. Check the
Mortgage API console logs for `Simulated notification recorded` and inspect the
queue and SQL records. A successful submission is independent of Service Bus
availability; the outbox publishes later. Submissions made before the new
SQL-backed code was deployed are not backfilled.

The pipeline restores `Notifications__Enabled=true` on each deployment; a
manual disable is therefore temporary. New SQL submissions accumulate outbox
rows while disabled and will be published after re-enabling. Local file and
in-memory providers do not create outbox events; the workers require SQL Server.

### One-time GitHub deployment permission

The existing GitHub deployment app already needs **Contributor** on the
resource group. An Azure administrator with permission to grant role assignments
must also grant its service principal **Role Based Access Control Administrator**
on that same resource group, with a condition restricting it to the two Service
Bus data roles and the Mortgage API managed identity. Contributor alone cannot
create the queue-scoped role assignments.

In Azure Portal, open the resource group's **Access control (IAM) → Add role
assignment**. Select **Role Based Access Control Administrator**, select the
existing GitHub deployment service principal, and on the **Conditions** tab
select **Allow user to only assign selected roles to selected principals** →
**Select roles and principals** → **Constrain roles and principals**. Allow
only **Azure Service Bus Data Sender** and **Azure Service Bus Data Receiver**, and only the
`mortgage-demo-env-mortgages` managed identity (use its principal/object ID,
not its client ID). Review the scope and condition before assigning. See
[Microsoft's delegation examples](https://learn.microsoft.com/azure/role-based-access-control/delegate-role-assignments-examples#example-constrain-roles-and-specific-groups).
Check for other unconditioned role-assignment permissions on the same identity:
an additional broad assignment can override this restriction.

The deployment identity's Contributor access remains powerful, and even the
conditioned role permits these two data roles for this identity anywhere within
the resource group. Protect `main` and restrict workflow edits. Role propagation
can take several minutes; if the workflow fails due to permissions, correct
them and rerun the failed job. This permission must be granted again after
deleting and recreating the resource group or managed identity.

### Monitoring, retries and cost

The publisher checks SQL approximately every 30 seconds per running replica;
a failed or interrupted claim becomes available after its five-minute lease.
Sending followed by a crash can produce duplicates. The SQL simulation record
deduplicates by event ID, but this is **at-least-once**, not exactly-once
delivery. Processing errors are retried by Service Bus and eventually
dead-lettered; invalid events are dead-lettered immediately. Active messages
expire after 14 days and are moved to the dead-letter queue. Dead-lettered
messages are not automatically deleted.

Inspect Mortgage API error logs, unpublished `SubmissionOutbox` rows, the queue's
active/dead-letter counts, and `SimulatedNotifications` in the private mortgage
database. In the Azure portal, use Service Bus Explorer with suitable Entra
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

From the repository root, run:

```bash
bash infra/quickstart-deploy.sh
```

Confirm the subscription and resource group carefully. The script deploys the
foundation, assigns the customer managed identity its API role, builds six images
in Azure Container Registry, runs the database bootstrap job inside the VNet,
then deploys the apps and checks their public readiness endpoints. It exits on
failure; partial deployments remain billable. On retry, use the same resource
names and the same API keys. Save the names shown by the script. It detects an
existing SQL server in the selected resource group and omits the creation-only
administrator settings on subsequent deployments.

Add the resulting staff URL's `/signin-oidc` and `/signout-callback-oidc` paths
to the staff app registration before testing sign-in. Readiness checks do not
verify sign-in, document uploads or a complete submission. Test those separately
with fictional data.

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
the **existing** quickstart deployment and applies the notification Bicep and
settings. It does not create the resource group, core infrastructure or Entra
registrations, or manage secrets. Review other infrastructure changes and deploy
them separately.

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
   builds all six images because quickstart images do not contain a Git commit
   baseline. Later runs compare each service's last successful deployed commit
   against the new commit, including transitive project references and linked
   files. If a baseline is unavailable, the service is rebuilt rather than
   silently skipped. A failed deployment must pass on a later retry before its
   new image is recorded as successful.

If the database bootstrap project or its dependencies change, the workflow
builds and runs the existing private Container Apps job before updating apps.
Migrations must remain compatible with the currently running revisions; use
expand-and-contract migrations. The pipeline checks public readiness and API
dependencies, not a full sign-in, upload or submission. It stores successful
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
- After correcting the configuration, select **Re-run jobs → Re-run failed
  jobs** on the GitHub Actions run. Readiness checks do not replace testing
  staff sign-in and a full journey with fictional data.

### After deleting the resource group

The workflow fails if the group is missing or has been recreated empty; it does
not provision the registry, apps, job or databases. To recreate the prototype:

1. Create the resource group in the intended subscription and region, then run
   the quickstart as above. Resource names must be globally available.
2. Reassign the GitHub deployment identity's Contributor and constrained
   role-assignment permissions on the recreated group, using the new Mortgage
   API managed identity principal ID. The Entra app registration and its
   federated credential are outside the group and can be reused.
3. Update the GitHub resource-group and registry variables if their names
   changed. Add any new staff URL callback paths to Entra and test sign-in.
4. Push or merge a change to `main`, or rerun the appropriate failed workflow
   for the current `main` commit from GitHub Actions. The new resources lack
   successful Git commit markers, so the first workflow deployment builds all
   images and runs the bootstrap job.

GitHub serializes workflow deployments, but cannot serialize a manual
quickstart run. Avoid running them against the same group simultaneously.

## When finished

If data is disposable, delete the explicitly selected resource group in Azure
Portal. This permanently deletes its databases, blobs, identities and other
resources; ensure nothing else in the group needs to be retained. Verify deletion
has finished and check for resources or charges outside that group. Entra app
registrations and subscription-level budgets are not deleted with the group.

Alternatively, after checking the selected subscription and group, use Azure CLI:

```bash
az account show --output table
az group show --name "<resource-group>" --output table
az group delete --name "<resource-group>"
az group exists --name "<resource-group>"
```

Confirm the deletion prompt only after verifying the group. The final command
should return `false` once deletion has completed.

The older scripts in [infra/scripts](infra/scripts/README.md) are not verified
pause/resume or cost-estimation tools; do not use them to control costs.
