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
   GitHub workflows.
3. **Already completed one-time setup?** Go straight to
   [run the workflows](#run-the-workflows). You do not need to recreate the
   Entra registrations or re-enter the stored GitHub secrets each time.
4. **Deleted the entire resource group?** The workflows cannot recreate it or
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

Image builds, releases, infrastructure changes and both on-demand workflows share a
GitHub concurrency group. Do not start a manual quickstart concurrently, and
avoid queuing multiple lifecycle runs: GitHub may replace a pending run.
Pushes to `main` never deploy applications. Image builds are skipped when the
registry is absent, but a missing registry does not prove that all billable
resources are gone. Readiness does not test sign-in, uploads or a full submission.

## Release changes to an existing prototype

These workflows are new and **have not been validated against live Azure**.
Use fictional data, review backups and costs, and test the complete journey
before an interview. A push builds images but does not deploy apps, apply
infrastructure or migrate databases. The create workflow remains a whole
environment operation; do not use it as a routine app update.

### 1. Prepare and build

1. Make and test changes locally. Review dependencies between services,
   configuration, identities and database changes. Commit and push to `main`.
2. In GitHub open **Actions → Build, test and security checks** and select the
   run for your commit. Wait for all jobs to succeed. Its **build-images** job
   builds affected projects and records registry image digests in a
   `release-manifest-<attempt>` artifact. Project references and linked files
   are considered; a missing or non-ancestor push baseline causes all images
   to be built. Pull requests run checks but do not publish images.
3. If the registry or resource group is absent, the image build is skipped:
   create the prototype first. To build **all seven** images for the current
   `main` commit, choose **Actions → Build, test and security checks → Run
   workflow → main → Run workflow** and wait for success. Do this after
   recreating the registry, or when an incremental run did not build the
   target you want. Builds and stored images incur charges.
   If metadata access fails, check the
   [registry permission](docs/azure-on-demand-setup.md#check-registry-access-for-image-digests).
4. Open the successful build run and copy its numeric run ID from the URL
   (`.../actions/runs/<run-id>`). The target must be listed in its manifest;
   an unchanged app may require an earlier successful build run or a full build.
   The manifest is retained according to GitHub artifact retention settings.
   If it has expired or its digest was removed from ACR, run a new full build.
   Selecting an older run means deploying its older code, so review
   compatibility first. The workflow verifies the run, commit, registry,
   target and digest; a successful test run without a manifest is not a release.

### 2. Review and apply infrastructure only when required

Existing infrastructure changes and new services require code changes, not
just pressing a button. The narrowly scoped
[`infra/changes.bicep`](infra/changes.bicep) starts empty. It is for **new
resources**, not the full create template. Reference existing resources using
`existing`; do not redeclare existing apps, databases or networks. Nested
deployments/modules are rejected. The workflow supplies `releaseCommit` as
the current commit; a new Container App can reference an image tag built for
that commit, but that initial tag is mutable and must be reviewed. Creation
of a new app starts its initial image; later releases use immutable digests.
Never put secrets in the template or preview output.

1. Add and review the new resource definition in `infra/changes.bicep`. Also
   update `infra/main.bicep` or its modules and quickstart for full
   recreation, the explicit deletion allowlist and tests. For a new app,
   add its project/image mapping in `infra/deployment/services.sh`, its
   release choice in `.github/workflows/prototype-release.yml`, image-build
   selection and any permissions, networking and configuration. The existing
   deployment identity may not be authorized to create new role assignments:
   ask an administrator to review the least-privilege setup, not broaden it
   automatically.
2. Commit to `main`, wait for the full build described above if an initial
   image is needed, then select **Actions → Preview or apply prototype
   infrastructure → Run workflow → main**. Choose **preview** and run it.
3. Open the run and review the listed resource IDs and change types, the
   committed Bicep and its compiled properties against your intended change.
   To inspect the compiled template locally, run
   `az bicep build --file infra/changes.bicep --stdout` from the repository root.
   The log lists IDs and types, not full property values. Independently review
   costs, identities, access, networking and effects on existing resources.
   Azure what-if is a prediction, not a guarantee: a new role assignment or
   resource can affect an existing service even when existing resources are
   not redeployed.
4. Only after review, copy the preview's run ID. Run **Preview or apply
   prototype infrastructure** again on `main`, choose **apply**, enter that
   preview run ID and start it. The workflow repeats what-if and requires the
   same commit, subscription, resource group, compiled template and preview
   result. A changed result requires a new preview and review. Apply is
   incremental and rejects predicted modifications, redeployments and
   unsupported or indeterminate changes. `Ignore` entries are allowed for
   unrelated resources, but what-if can also mark resources `Ignore` when
   expansion limits are reached; review for incomplete previews. Rerunning
   after creation may be rejected as `NoChange`.
5. Inspect the Azure resources and logs. A failed or partial apply is not
   automatically rolled back; inspect before retrying. Changing an **existing**
   resource is deliberately outside this workflow: plan and review a
   separate, resource-specific change and backup rather than applying the
   full create template as an isolated update.

### 3. Run migrations deliberately when required

1. Review all four database migrations and historical notification-copy
   behaviour in the selected bootstrap commit. Back up data you need.
   Use expand-and-contract: the new schema must support both currently running
   and planned app versions. Neither tests nor the workflow prove this.
2. In **Actions → Run prototype database bootstrap → Run workflow**, choose
   `main`, enter the successful build run ID containing `database-bootstrap`,
   and run it. The workflow updates the existing private job to the verified
   digest, starts it and waits for success.
3. Wait for green before releasing any dependent app. On failure or timeout,
   inspect the named Azure job execution and its logs. A timed-out execution
   may still be running; do not start another until its state is understood.
   Migrations may have partially applied and are not automatically reversed.
   Fix or recover before proceeding. The bootstrap is not run automatically
   for app deployments.

### 4. Deploy and verify one app at a time

1. Check that the selected target already exists and is compatible with its
   dependencies, configuration and current database. For older API-hosted
   notifications, use the [legacy cutover](docs/azure-advanced-deployment.md#enable-on-an-existing-quickstart-deployment).
2. Select **Actions → Deploy one prototype target → Run workflow → main**.
   Enter the build run ID and select one of `products-api`, `eligibility-api`,
   `mortgage-api`, `mortgages-web`, `staff-web` or `notifications-worker`.
   Leave **allow rollback** off for a normal forward release. Start the run.
3. Wait for success, then inspect the selected Container App's revision and
   logs. Public web apps receive a readiness check; internal APIs and the
   worker require manual verification. Exercise dependencies, staff sign-in,
   a fictional customer and staff submission, upload and notifications as
   appropriate. Repeat for each other changed app, in a dependency-compatible
   order. No other app, notification infrastructure or migration is updated by
   this release action.
4. To roll back, first confirm the old image still exists and is compatible
   with the **current** schema and other services. Select its successful build
   run, target, and explicitly enable **allow rollback**. A failed readiness
   check can happen *after* Azure accepted an update; it does not roll back
   automatically and its commit tag may still show the previous release.
   Inspect the actual revision and image before deciding what to do. Database
   migrations are never reversed by an image rollback.

GitHub prevents overlapping running workflows in the shared concurrency
group, but a newer queued run can replace an older pending one. Check every
run's status. An independently started interactive quickstart is not covered
by that lock; never run it concurrently with these workflows.

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
5. Test customer access and staff sign-in. Create already ran bootstrap and
   deployed images from its source revision. To prepare immutable images for
   later individual releases, run the full build described in the
   [release guide](#release-changes-to-an-existing-prototype). Old build
   artifacts may refer to images deleted with the previous registry.

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
