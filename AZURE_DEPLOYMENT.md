# Azure prototype deployment

The quickstart deploys two public web apps, three internal APIs, three SQL databases,
and private SQL and Blob endpoints in one VNet. This is a prototype: use fictional
data only. The quickstart has deployed and staff sign-in has been exercised in
one Azure environment; a complete customer-to-staff journey and the GitHub
Actions deployment workflow have not been validated end-to-end.

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
the **existing** quickstart deployment. It does not create a resource group,
apply Bicep, change app configuration or manage secrets. Deploy infrastructure
changes separately and review them before applying them.

1. In Entra, create a **separate** app registration for deployment. On it, open
   **Certificates & secrets → Federated credentials**, add a GitHub Actions
   credential for organisation `eyeballpaul`, repository
   `Mortgage-Processing-Application`, entity type **Branch**, and branch
   `main`. The expected subject is
   `repo:eyeballpaul/Mortgage-Processing-Application:ref:refs/heads/main`.
   Grant its service principal **Contributor** on the existing resource group.
   The workflow uses OIDC and does not need a client secret. Protect `main` and
   restrict who can change the workflow: the federated identity can update the
   resources in that group.
2. In GitHub, under **Settings → Secrets and variables → Actions → Variables**,
   add `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
   `AZURE_RESOURCE_GROUP` and `AZURE_CONTAINER_REGISTRY`. Use the deployment
   service principal's client ID and the existing resource names, not the staff
   app client ID. Do not put secret values in these variables.
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

### After deleting the resource group

The workflow fails if the group is missing or has been recreated empty; it does
not provision the registry, apps, job or databases. To recreate the prototype:

1. Create the resource group in the intended subscription and region, then run
   the quickstart as above. Resource names must be globally available.
2. Verify and, if necessary, reassign the GitHub deployment identity's
   Contributor role on the recreated group. The Entra app registration and its
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

The older scripts in [infra/scripts](infra/scripts/README.md) are not verified
pause/resume or cost-estimation tools; do not use them to control costs.
