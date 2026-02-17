# Azure Infrastructure Baseline

This folder contains the minimum Azure footprint required to operate Ops Copilot in enterprise tenants. The current scope keeps the Log Analytics workspace and cost guardrails as the foundation and adds an **optional** Azure AI Foundry stack for enterprise engineering scenarios.

## Resource Topology

| Component | Description |
| --- | --- |
| Log Analytics | `bicep/modules/log-analytics.bicep` provisions a workspace (PerGB2018 SKU, 30‑day retention by default) used by API hosts and worker telemetry. |
| Cost Budgets | `bicep/modules/budget.bicep` enforces spending guardrails at the resource-group scope with configurable thresholds and email alerts. |
| Foundry Config Toggle | `bicep/modules/foundry.bicep` stores the desired Azure AI Foundry account/project metadata. Actual provisioning happens through CLI because Foundry does not yet expose a stable ARM/Bicep contract. |

`bicep/main.bicep` stitches these modules together and surfaces shared outputs for monitoring pipelines.

## Prerequisites & Roles

| Requirement | Notes |
| --- | --- |
| Azure subscription + resource group | The workflow deploys at resource-group scope via `az deployment group create`. |
| Azure CLI 2.62+ with Bicep 0.29+ | Needed locally and inside GitHub Actions runners. |
| GitHub Actions OIDC | The workflow uses `azure/login@v2` with federated credentials only. No client secrets are stored. |
| RBAC for base resources | `Contributor` (or equivalent) on the target resource group. |
| RBAC for Foundry stage | Preview service currently expects **Azure AI Developer** + **Cognitive Services Contributor** or **Owner** to create accounts/projects via CLI. |
| az extension | The workflow adds the `ml` extension to call the preview `Microsoft.ProjectBabylon` API surface. |

## Parameters & Environment Files

Per-environment parameter files live in `bicep/env/`. See [`env/dev.parameters.json`](bicep/env/dev.parameters.json) and [`env/sandbox.parameters.json`](bicep/env/sandbox.parameters.json) for examples. Common parameters:

- `workspaceName`, `location`, `tags`: Log Analytics metadata.
- `budgetName`, `budgetAmount`, `budgetStartDate`, `budgetEndDate`, `budgetContactEmails`: Cost guardrails that remain mandatory for every environment.
- `enableFoundry`: Toggles the Foundry CLI stage (defaults to `false`).
- `foundryName`, `foundryProjectName`: Placeholders for the Foundry account and project. They are required only when `enableFoundry` is `true`.
- `modelProvider`: Object that captures endpoint metadata (`endpointName`, `modelName`) for the Foundry pipeline to configure downstream model access.

## Cost Guardrails

1. **Budgets remain required** – every parameter file must define a budget amount and alert recipients. Notifications fire at 80/90/100% by default.
2. **Telemetry retention capped** – the Log Analytics module defaults to 30 days to keep ingestion costs predictable.
3. **Foundry staging limited** – the Foundry toggle remains `false` for dev and only turns on for sandbox/enterprise engineering. Extra spend is isolated behind that explicit switch, and the pipeline will refuse to run the Foundry stage if the toggle is `false`.

## Foundry Enablement Flow

1. Adjust the environment parameter file:
   - Set `enableFoundry` to `true`.
   - Provide `foundryName`, `foundryProjectName`, and a `modelProvider` (endpoint + model deployment name).
2. Run the GitHub Actions workflow with the target environment input (`dev`, `sandbox`, etc.).
3. The base job deploys Bicep artifacts (Log Analytics + budget) and exports Foundry settings.
4. When `enableFoundry=true`, the `foundry` job executes preview Azure CLI commands to:
   - Ensure the `ml` extension is available.
   - Create or update the Foundry account (`Microsoft.ProjectBabylon/accounts`).
   - Create or update the Foundry project.
   - Register model endpoint access by wiring the provided `modelProvider` info into the project connections.

Disabling Foundry simply flips the toggle back to `false` in the relevant parameter file; no CLI stage will be triggered, keeping dev costs minimal.

## Running the Deployment Pipeline

The GitHub Actions workflow file is stored at [`pipelines/deploy-infra.yml`](pipelines/deploy-infra.yml).

- Trigger on `push` to `main` (infra files only) or via `workflow_dispatch`, passing the `environment` input (`dev` default).
- Uses repository/environment variables (`AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`, `AZURE_RG_DEV`, `AZURE_RG_SANDBOX`, etc.) with federated credentials configured in Azure AD.
- Base job executes: `az deployment group create --resource-group <RG> --template-file bicep/main.bicep --parameters @bicep/env/<env>.parameters.json`.
- Foundry job consumes the parsed toggle and issues the preview CLI commands wrapped in retry-friendly shell scripts.

## Operational Notes

- Until Microsoft publishes a stable Foundry resource provider, the CLI stage is the single source of truth for Foundry provisioning.
- The workflow keeps the Foundry job isolated so it can be easily re-run without touching shared telemetry resources.
- Budget thresholds and recipients should be reviewed monthly to maintain accurate guardrails as usage patterns evolve.
