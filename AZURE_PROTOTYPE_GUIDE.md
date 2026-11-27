# Azure prototype: create, delete and recreate

The quickstart deploys two public web apps, three internal APIs, a notification worker, four SQL databases,
and private SQL and Blob endpoints in one VNet. This is a prototype: use fictional
data only. An earlier version of the quickstart, staff sign-in and GitHub
Actions deployment succeeded in one Azure environment. The separate worker,
its scale rule and the updated workflows have not yet been validated live.

For detailed first-time setup, see the [setup-from-scratch guide](docs/azure-setup-from-scratch.md).

## Choose where to start

1. **New to this project or Azure?** Follow the
   [setup-from-scratch guide](docs/azure-setup-from-scratch.md) in order. It
   explains accounts, tools, Entra registrations and the first deployment.
2. **Already completed the first deployment?** Follow
   [one-time setup](docs/azure-on-demand-setup.md#one-time-setup) to enable the
   two GitHub buttons.
3. **Already completed one-time setup?** Go straight to
   [run the workflows](#run-the-workflows). You do not need to recreate the
   Entra registrations or re-enter the stored GitHub secrets each time.
4. **Deleted the entire resource group?** The buttons cannot recreate it or
   the managed identities. Follow [resource-group recreation](#after-deleting-the-resource-group)
   and repeat the one-time setup if required.
5. **Need the manual quickstart, notification operations or application-only
   deployment?** See the
   [advanced deployment guide](docs/azure-advanced-deployment.md).

Throughout these guides, replace text inside `<angle-brackets>` with your own
value, without the brackets. A **client ID** identifies an application; an
**object ID** identifies a particular directory object. They are not
interchangeable. A **secret value** is a password: never put it in Git, a
repository variable, a screenshot, an issue or chat. Portal labels can change;
stop and check rather than guessing if a step looks different.

## On-demand GitHub workflows

The **Create prototype** and **Delete prototype resources** workflows are an
alternative to running the interactive quickstart. They require the one-time
setup linked above. Earlier versions of creation and deletion were tested
against live Azure; deletion required a retry because a DNS link was still
being removed. The updated worker and link deletion ordering have mock tests
but have not yet been tested live.

The delete workflow removes recognized prototype resources and their data,
including SQL databases, blobs, the Service Bus queue, images, and the Log
Analytics workspace and its logs. The workspace is permanently deleted so its
name can be reused. It retains the resource group, eight managed identities and
group-scoped permissions. If it finds an unrecognized resource or a resource
without the expected prototype tags, it stops **before deleting resources** and
lists the resource for review; a billable resource may remain. It removes the
recognized private DNS virtual network links before deleting the VNet and DNS
zones, and refuses unexpected links before deleting resources. It removes the
current deployed staff app's two callback URIs while preserving other staff
registration settings. Older callbacks from previous deployments must be
reviewed separately.

### Run the workflows

#### Create when needed

1. If an older prototype still has the consumer inside the Mortgage API, first
   follow the [legacy cutover](docs/azure-advanced-deployment.md#enable-on-an-existing-quickstart-deployment).
   The create workflow cannot upgrade it in place. Do not delete data you need.
2. Make sure the workflow and script changes you intend to use have been
   committed and pushed to `main`. In GitHub, open the repository →
   **Actions → Create prototype**. Select
   **Run workflow**, choose `main`, and select **Run workflow** again.
   If the workflow or button is missing, check that the files are on the
   default branch and that GitHub Actions is enabled for the repository.
3. Open the new run. Wait for a green check; building images and creating
   Azure resources can take time and incur charges. If it fails, open the
   failed step, read the first useful error and follow
   [troubleshooting](#workflow-troubleshooting). Partial resources may remain.
4. On the successful run's **Summary**, open **Customer app** and confirm the
   page loads. Open **Staff app**, sign in as an assigned staff user, and
   confirm it loads.
5. Submit an application with **fictional data only** and check it appears in
   Staff Web. For notifications, follow the
   [worker verification steps](docs/azure-advanced-deployment.md#check-a-submission-notification).
   Processing may take time; **no email is sent**. Also test a staff
   submission and document upload if needed.

#### Delete when finished

**This permanently deletes prototype databases, blobs, queue data, images and
logs.** Back up anything needed first. The resource group, eight identities,
Entra registrations, GitHub configuration and group permissions remain.

1. In GitHub, open **Actions → Delete prototype resources → Run workflow**.
   Select `main`, then **Run workflow** again.
2. Open the new run and wait for a green check. A failure can leave billable
   resources behind. Read its log and follow
   [troubleshooting](#workflow-troubleshooting); do not assume cleanup succeeded.
3. In Azure Portal, open **Resource groups → your group → Resources** and
   confirm only the eight named managed identities remain. Check for additional
   resources and costs elsewhere in the subscription too.
4. Open **Cost Management + Billing → Cost analysis** at the subscription
   scope. Review charges by resource, and check again later because charges can
   arrive late. The budget is an alert, not a spending cap.

#### Workflow troubleshooting

1. Open the relevant run under **Actions**. Select its failed job and expand
   the first failed step. Share only the error code and message if asking for
   help; redact IDs where possible, and never share secrets, tokens or full
   authentication claims.
2. For `Insufficient privileges` before foundation deployment, verify the
   two Graph **application** permissions show **Granted**, the correct
   service principal has the assignments, and Staff Web ownership is correct.
   Check the specific failing operation before changing permissions.
3. If deletion reports an unrecognized resource, missing tag or unexpected DNS
   link, inspect it and its costs. The workflow deliberately stops; do not
   delete unrelated resources just to make it pass.
4. If Azure reports a conflict or operation in progress, check **Resource
   group → Activity log** for the failed operation's error code and message.
   Wait for any related operation to finish, correct the cause, then use
   **Re-run jobs → Re-run failed jobs**. Deletion can have partially completed.
5. After any failed or partial cleanup, check the resource group and Cost
   Management. For login and staff errors, see
   [GitHub deployment troubleshooting](docs/azure-advanced-deployment.md#github-deployment-troubleshooting)
   and [staff sign-in troubleshooting](docs/azure-advanced-deployment.md#staff-sign-in-troubleshooting).

The existing application deployment and both on-demand workflows share a
GitHub concurrency group. Do not start a manual quickstart concurrently, and
avoid queuing multiple lifecycle runs: GitHub may replace a pending run.
Successful pushes to `main` skip application deployment when the registry is
absent, but a missing registry does not prove that all billable resources are
gone. Readiness does not test sign-in, document uploads or a full submission.

### After deleting the resource group

The on-demand workflow fails if the group or retained identities are missing;
recreating an empty group alone is not enough. Deleting the group also removes
its Azure role assignments and permanently deletes its data. Entra app
registrations and GitHub configuration are outside the group and normally
remain. To recreate the prototype:

1. Check the existing Entra registrations, deployment federated credential,
   staff secret and service keys. Reuse them rather than creating duplicate
   registrations. Ensure the resource names are still globally available.
2. Follow [Deploy](docs/azure-advanced-deployment.md#deploy) using an
   appropriately authorized Azure user. Enter the missing group name and region.
   The quickstart creates the group after
   confirmation, rebuilds the infrastructure and identities, assigns the
   customer role and updates staff callbacks. It can incur charges.
3. If choosing its optional automatic GitHub setup, first sign in to GitHub
   CLI and confirm the existing repository variables and exact repository.
   It restores Contributor and a **queue-scoped** condition for sender and
   receiver assignments to the Mortgage API identity, and updates three
   repository variables. This is not sufficient for either the worker and
   scaler or the on-demand create workflow.
4. Repeat [one-time setup](docs/azure-on-demand-setup.md#one-time-setup),
   particularly the group-scoped condition using the **new** managed identity principal IDs, the customer
   app-role check, and all repository variables and secrets. If automatic
   GitHub setup was skipped, restore its Contributor permission and variables
   manually. Recheck Graph consent, ownership and the existing OIDC
   credential; they are outside the group but should not be assumed correct.
5. Test customer access and staff sign-in. The new resources lack successful
   Git commit markers, so the first regular workflow deployment builds all
   images and runs the bootstrap job. If using that workflow, push or merge a
   change to `main` or rerun an appropriate current-commit run.

GitHub serializes workflow deployments, but cannot serialize a manual
quickstart run. Avoid running them against the same group simultaneously.
This new automated recreation flow has not yet been tested against live Azure
and GitHub.

## When finished

For routine use, follow [Delete when finished](#delete-when-finished) to
remove recognized resources and retain the group and identities.

**Alternative: delete the entire resource group.** Choose this only if you
accept losing all its resources and data, including identities and group
permissions, and are prepared to repeat
[recreation](#after-deleting-the-resource-group). In Azure Portal:

1. Open **Resource groups**, select the correct subscription and group, and
   inspect everything inside it. Stop if anything must be retained.
2. Select **Delete resource group**, enter the group name to confirm, and
   select **Delete**. This cannot be undone.
3. Wait until it is no longer listed. Check subscription-level Cost Management
   for other resources, soft-deleted data and delayed charges. Entra app
   registrations and subscription budgets are not deleted with the group.

Alternatively, an experienced Azure CLI user can check the selected
subscription and group and then delete that exact group:

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
