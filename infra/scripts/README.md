# Legacy deployment scripts

The scripts in this directory are **not verified** for the current deployment.
Do not use them to deploy, pause or resume resources, retrieve URLs, or estimate
costs. In particular, they do not establish that the entire environment is
paused and their fixed cost estimates are unsupported. The pause and resume
scripts use `az containerapp replica count set`, which is not listed in the
current Azure CLI replica command reference; do not assume they stopped any apps.

Use the [Azure deployment guide](../../AZURE_DEPLOYMENT.md) for the current
quickstart, GitHub Actions workflow, cost guidance and resource-group deletion
procedure. There is currently no verified whole-environment pause/resume tool.
