# OpsCopilot Azure Infrastructure

## Overview

This folder provisions **enterprise-ready Azure infrastructure** for OpsCopilot across **two Azure subscriptions**, each representing a logical tenant boundary within a single Entra tenant.

| Logical Tenant | Azure Subscription | Subscription ID |
|---|---|---|
| TenantA | SubA | `b20a7294-6951-4107-88df-d7d320218670` |
| TenantB | SubB | `bd27a79c-de25-4097-a874-3bb35f2b926a` |

Subscription IDs are stored only in parameter files — **never in Bicep**.  
The pipeline uses a matrix to deploy to both subscriptions in parallel.

---

## Folder Structure

```
azure-infra/
  README.md
  bicep/
    main.bicep                   # Subscription-scope orchestrator
    modules/
      rg.bicep                   # Resource group
      tags.bicep                 # Consistent tag object
      logAnalytics.bicep         # Log Analytics Workspace
      appInsights.bicep          # Application Insights (workspace-based)
      keyVault.bicep             # Key Vault (Standard, RBAC)
      storage.bicep              # Storage Account (optional, default OFF)
      budget.bicep               # Subscription budget + alerts
      foundry.bicep              # Azure AI Foundry (best-effort Bicep + CLI)
    env/
      dev/
        tenantA.parameters.json
        tenantB.parameters.json
      sandbox/
        tenantA.parameters.json
        tenantB.parameters.json
      prod/
        tenantA.parameters.json
        tenantB.parameters.json
```

---

## SKU / Tier Policy

| Resource | SKU / Tier | Reason |
|---|---|---|
| Key Vault | Standard | No HSM/Premium; ~£0.03/10k ops |
| Storage Account | Standard_LRS | Lowest cost; no GRS/ZRS |
| Log Analytics | Pay-As-You-Go | 30-day retention, 0.1 GB/day quota |
| App Insights | Consumption | Workspace-based; ~£2.30/GB after 5 GB free |
| Budget | Native | No cost; built-in Azure feature |
| Azure AI Foundry | Free/Basic account | Token consumption cost; see Foundry section |

**Premium SKUs are strictly prohibited** across all modules.

---

## Cost Guardrails

### Log Analytics
- **Retention**: 30 days (configurable via `retentionInDays` parameter).
- **Daily quota**: 0.1 GB/day by default. Set to `-1` to disable quota (not recommended in dev).
- Estimated cost at 0.1 GB/day: ~£0.23/month.

### App Insights
- Workspace-based (data goes into LAW above).
- First 5 GB/month free; ~£2.30/GB after that.
- **Action**: Configure adaptive sampling in application code to stay within the free tier.
- App Insights itself has no SKU — cost is 100% driven by data volume ingested into LAW.

### Key Vault
- Standard SKU. ~£0.03 per 10,000 operations.
- Estimated dev cost: < £1/month.

### Storage Account
- **Default OFF** (`enableStorage: false`) to avoid unnecessary spend.
- When enabled: Standard_LRS, ~£0.016/GB/month.

### Subscription Budget
- Default: **£80/month** per subscription.
- Alert thresholds: 50% / 75% / 90% / 100%.
- Notification emails are configured via the `budgetEmails` parameter array.
- Budgets are informational only (no auto-shutdown). Combine with Azure Policy for enforcement.

### Azure AI Foundry (Cost Risk)
- **TenantA** (`foundryEnabled: true`): Foundry account provisioned + smallest available model deployed. Token consumption is pay-per-use. **Risk**: unbounded token spend if the application calls the model heavily. Mitigate by setting a separate per-model budget or throttle in application config.
- **TenantB** (`foundryEnabled: false`): Foundry account is provisioned (structure exists) but **no model deployment** is created and **no RBAC permission** is granted to the application identity. This is the official guardrail — the application cannot call Foundry even if misconfigured.
- Safe defaults: use `gpt-4o-mini` or equivalent smallest/cheapest model. Keep token limits low.

---

## OIDC Setup (GitHub Actions — No Secrets)

### 1. Create an App Registration per subscription

```bash
# Repeat for each subscription (SubA and SubB)
az ad app create --display-name "opscopilot-deploy-<env>"
```

Note the `appId` (client ID) output.

### 2. Create a Service Principal

```bash
az ad sp create --id <appId>
```

### 3. Add a Federated Credential (OIDC)

In the Azure portal → App Registration → Certificates & secrets → Federated credentials → Add:
- **Issuer**: `https://token.actions.githubusercontent.com`
- **Subject**: `repo:dineshtripathi/Ops-Copilot-Platform-Net:ref:refs/heads/main`
  - For environment-based approval: `repo:dineshtripathi/Ops-Copilot-Platform-Net:environment:prod`
- **Audience**: `api://AzureADTokenExchange`

Or via CLI:
```bash
az ad app federated-credential create --id <appId> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dineshtripathi/Ops-Copilot-Platform-Net:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

### 4. Assign Roles on Each Subscription

```bash
# Contributor — for resource provisioning
az role assignment create \
  --assignee <appId> \
  --role Contributor \
  --scope /subscriptions/<subscriptionId>

# Budget RBAC — Cost Management Contributor (required for budgets)
az role assignment create \
  --assignee <appId> \
  --role "Cost Management Contributor" \
  --scope /subscriptions/<subscriptionId>
```

> **Foundry extra roles**: If Foundry is deployed via CLI in the pipeline, the SP also needs  
> `Cognitive Services OpenAI Contributor` (or `Cognitive Services Contributor`) at the resource group scope.

### 5. Store GitHub Secrets

In your GitHub repository → Settings → Secrets and variables → Actions:

| Secret | Value |
|---|---|
| `AZURE_CLIENT_ID_TENA` | App Registration `appId` for SubA |
| `AZURE_TENANT_ID` | Your Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID_TENA` | SubA subscription ID |
| `AZURE_CLIENT_ID_TENB` | App Registration `appId` for SubB |
| `AZURE_SUBSCRIPTION_ID_TENB` | SubB subscription ID |

> If you use a single multi-subscription SP, you can share `AZURE_CLIENT_ID` and set per-matrix `subscriptionId`.

---

## Running Locally

### Prerequisites
- Azure CLI ≥ 2.56
- Bicep CLI (`az bicep install`)
- Contributor + Cost Management Contributor on both subscriptions

### What-If (dry run)

```bash
az login
az account set --subscription b20a7294-6951-4107-88df-d7d320218670

az deployment sub what-if \
  --location uksouth \
  --template-file azure-infra/bicep/main.bicep \
  --parameters @azure-infra/bicep/env/dev/tenantA.parameters.json \
  --parameters location=uksouth environment=dev
```

### Deploy

```bash
az deployment sub create \
  --location uksouth \
  --template-file azure-infra/bicep/main.bicep \
  --parameters @azure-infra/bicep/env/dev/tenantA.parameters.json \
  --parameters location=uksouth environment=dev
```

Repeat with `tenantB.parameters.json` and `--subscription bd27a79c-de25-4097-a874-3bb35f2b926a` for TenantB.

---

## How to Change Environments

1. Update the relevant `env/<ENV>/tenant*.parameters.json` files.
2. Trigger the workflow via **workflow_dispatch** in GitHub Actions:
   - Select `ENV` (dev | sandbox | prod)
   - Select `REGION` (default: uksouth)
3. The pipeline will select the correct parameter files automatically.

**No Bicep code changes are required** when switching environments.

---

## How to Add a Third Subscription / Tenant

1. Create a new parameter file: `env/<ENV>/tenantC.parameters.json`.
2. Add a third entry to the pipeline matrix in `.github/workflows/deploy-infra.yml`:
   ```yaml
   - tenantLabel: TenantC
     subscriptionId: ${{ secrets.AZURE_SUBSCRIPTION_ID_TENC }}
     clientId: ${{ secrets.AZURE_CLIENT_ID_TENC }}
     paramFile: azure-infra/bicep/env/${{ inputs.ENV }}/tenantC.parameters.json
   ```
3. Create the App Registration, Service Principal, and OIDC federated credential for the new subscription (see OIDC Setup above).
4. Add the new GitHub secrets.

---

## Foundry: Provisioned-Only Guardrail (TenantB)

When `foundryEnabled: false`:
- The Azure AI Foundry **account resource is created** (structure + billing baseline only).
- **No model deployment** or inference endpoint is created.
- **No RBAC role assignment** (`Cognitive Services OpenAI User`) is granted to the application identity.
- The application cannot call any Foundry API even if it has the endpoint URL, because it has no identity permission.
- To enable TenantB later: set `foundryEnabled: true` in `tenantB.parameters.json` and re-run the pipeline.

### Why Bicep + CLI hybrid for Foundry?

The `Microsoft.CognitiveServices/accounts` resource type (used by Azure AI Foundry / Azure OpenAI) is **stable in Bicep**. However, AI Foundry *Projects* (`Microsoft.MachineLearningServices/workspaces` with kind `Project`) require a Hub and can have version skew. The pipeline therefore:
1. Provisions the Foundry **account** (OpenAI resource) via Bicep.
2. Creates the **model deployment** (e.g., `gpt-4o-mini`) via `az cognitiveservices account deployment create` CLI — only when `foundryEnabled=true`.

This avoids Bicep resource-type instability for model deployments while keeping the account IaC-managed.

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `AuthorizationFailed` on budget | Missing Cost Management Contributor | Assign role (see OIDC Setup) |
| `kvName already exists` globally | Key Vault names are globally unique | Change `keyVaultName` in parameter file |
| `StorageAccountAlreadyExists` | Storage account names globally unique | Change `storageNamePrefix` to something unique |
| `QuotaExceeded` on Foundry | Model deployment quota not available in region | Request quota increase or change region |
| OIDC `short-lived token` error | Subject mismatch in federated credential | Verify branch/environment subject matches exactly |
| `InvalidTemplateDeployment` scope error | main.bicep used at wrong scope | Always deploy with `az deployment sub create`, not `az deployment group create` |
| LAW daily quota reached | `dailyQuotaGb` too low | Temporarily raise quota in parameter file; default 0.1 GB is very restrictive |
