# Legacy deployment scripts

The scripts in this directory are **not verified** for the current deployment.
Do not use them to deploy, pause or resume resources, retrieve URLs, or estimate
costs. In particular, they do not establish that the entire environment is
paused and their fixed cost estimates are unsupported. The pause and resume
scripts use `az containerapp replica count set`, which is not listed in the
current Azure CLI replica command reference; do not assume they stopped any apps.

Use the [prototype lifecycle guide](../../AZURE_PROTOTYPE_GUIDE.md) for routine
GitHub workflows and deletion, or the
[advanced deployment guide](../../docs/azure-advanced-deployment.md) for the
current quickstart and cost guidance. There is currently no verified
whole-environment pause/resume tool.

## What to do instead

1. For a first deployment, follow the
   [setup-from-scratch guide](../../docs/azure-setup-from-scratch.md).
2. To create an already configured prototype, follow
   [Create when needed](../../AZURE_PROTOTYPE_GUIDE.md#create-when-needed).
3. To stop paying for recognized prototype resources when finished, follow
   [Delete when finished](../../AZURE_PROTOTYPE_GUIDE.md#delete-when-finished).
   This permanently deletes prototype data; it is not a pause.
4. Verify the run succeeded and check subscription-level Cost Management,
   including delayed or external charges. A budget is an alert, not a cap.
