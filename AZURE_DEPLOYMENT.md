# Azure prototype deployment

The quickstart deploys two public web apps, three internal APIs, three SQL databases,
and private SQL and Blob endpoints in one VNet. This is a prototype: use fictional
data only. It has not been validated end-to-end in an Azure subscription.

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
names and the same API keys. Save the names shown by the script.

Add the resulting staff URL's `/signin-oidc` and `/signout-callback-oidc` paths
to the staff app registration before testing sign-in. Readiness checks do not
verify sign-in, document uploads or a complete submission. Test those separately
with fictional data.

## When finished

If data is disposable, delete the explicitly selected resource group in Azure
Portal. This permanently deletes its databases, blobs, identities and other
resources; ensure nothing else in the group needs to be retained. Verify deletion
has finished and check for resources or charges outside that group. Entra app
registrations and subscription-level budgets are not deleted with the group.

The older scripts in [infra/scripts](infra/scripts/README.md) are not verified
pause/resume or cost-estimation tools; do not use them to control costs.
