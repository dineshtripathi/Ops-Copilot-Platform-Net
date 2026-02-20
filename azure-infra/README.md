# OpsCopilot Azure Infrastructure

## Overview

Infrastructure is split across **two Azure subscriptions** by function:

| Subscription | Label | ID | Purpose |
|---|---|---|---|
| SubA | Platform | `b20a7294-6951-4107-88df-d7d320218670` | Compute, databases, networking |
| SubB | AI | `bd27a79c-de25-4097-a874-3bb35f2b926a` | AI services, embeddings, vector search |

Both subscriptions share the same **Entra tenant**: `4a72b866-99a4-4388-b881-cef9c8480b1c`.

---

## Folder Structure

```
azure-infra/
  README.md
  bicep/
    main.platform.bicep            # SubA orchestrator — Platform resources
    main.ai.bicep                  # SubB orchestrator — AI resources
    modules/
      rg.bicep                     # Resource group
      logAnalytics.bicep           # Log Analytics Workspace
      appInsights.bicep            # Application Insights (workspace-based)
      keyVault.bicep               # Key Vault (Standard, RBAC)
      storage.bicep                # Storage Account (Standard_LRS)
      budget.bicep                 # Subscription budget + alerts
      sql.bicep                    # Azure SQL Server + Database (Basic 5 DTU)
      containerAppsEnv.bicep       # Container Apps Managed Environment (Consumption)
      containerApp.bicep           # Generic Container App (SystemAssigned identity included)
      containerAppRbac.bicep       # Role assignments: KV Secrets User + LAW Reader for CAs
      qdrant.bicep                 # Qdrant vector DB as Container App + Azure Files
      aoai.bicep                   # Azure OpenAI account (NO model deployments in Bicep)
      search.bicep                 # Azure AI Search (optional — default OFF)
      tags.bicep                   # Tag schema reference (tags inlined in main files)
    env/
      dev/
        platform.parameters.json
        ai.parameters.json
      sandbox/
        platform.parameters.json
        ai.parameters.json
      prod/
        platform.parameters.json
        ai.parameters.json
  scripts/
    cleanup.sh                     # Bash — delete old TenantA/TenantB RGs
    cleanup.ps1                    # PowerShell — same, for local Windows use
```

---

## Resource Inventory

### SubA — Platform (`rg-opscopilot-platform-{env}-uks`)

| Resource | Module | SKU | Est. Cost |
|---|---|---|---|
| Log Analytics Workspace | `logAnalytics.bicep` | PerGB2018, 0.1 GB/day quota | ~£0.23/mo |
| Application Insights | `appInsights.bicep` | Workspace-based | ~£0 (5 GB free) |
| Key Vault | `keyVault.bicep` | Standard | ~£0.03/10k ops |
| Storage Account | `storage.bicep` | Standard_LRS | ~£0.016/GB |
| Azure SQL Server | `sql.bicep` | — | — |
| Azure SQL Database | `sql.bicep` | Basic (5 DTU, 2 GB) | ~£4.20/mo |
| Container Apps Env | `containerAppsEnv.bicep` | Consumption | £0 (idle) |
| Container App: apihost | `containerApp.bicep` | Scale-to-zero | £0 (idle) |
| Container App: workerhost | `containerApp.bicep` | Scale-to-zero | £0 (idle) |
| Container App: mcphost | `containerApp.bicep` | Scale-to-zero | £0 (idle) |
| Container App: qdrant | `qdrant.bicep` | Scale-to-zero + Azure Files | ~£0.50/mo storage |
| Budget | `budget.bicep` | — | £0 |

**SubA total estimated dev spend: ~£6–10/month** (plus traffic-driven Container Apps costs).

### SubB — AI (`rg-opscopilot-ai-{env}-uks`)

| Resource | Module | SKU | Est. Cost |
|---|---|---|---|
| Log Analytics Workspace | `logAnalytics.bicep` | PerGB2018, 0.1 GB/day quota | ~£0.23/mo |
| Application Insights | `appInsights.bicep` | Workspace-based | ~£0 (5 GB free) |
| Key Vault | `keyVault.bicep` | Standard | ~£0.03/10k ops |
| Azure OpenAI account | `aoai.bicep` | S0 | token pay-per-use |
| Azure AI Search | `search.bicep` | Free (default OFF) | £0 / ~£65 (basic) |
| Budget | `budget.bicep` | — | £0 |

**SubB total estimated dev spend: ~£1–5/month + token consumption** (AOAI model deployments NOT created by default).

---

## Toggles Reference

### Platform toggles (`main.platform.bicep` / `platform.parameters.json`)

| Parameter | Default | Effect |
|---|---|---|
| `enableStorage` | `true` | `false` skips storage account and Qdrant |
| `budgetEmails` | `[]` | Add email addresses to receive budget alerts |
| `bootstrapImage` | hello-world | Placeholder image; overridden by app deployment pipeline |

### AI toggles (`main.ai.bicep` / `ai.parameters.json`)

| Parameter | Default | Effect |
|---|---|---|
| `enableLaw` | `true` | `false` skips Log Analytics Workspace |
| `enableAppInsights` | `true` | `false` (or `enableLaw=false`) skips App Insights |
| `deploymentsEnabled` | `false` | `true` instructs pipeline to create AOAI model deployments via CLI |
| `searchProvision` | `false` | `true` creates Azure AI Search (`searchSku` applies) |
| `searchSku` | `free` | `basic` (~£65/mo) for prod; `free` for dev/sandbox |

### Cleanup toggle (`deploy-infra.yml`)

| Input | Default | Effect |
|---|---|---|
| `cleanupEnabled` | `true` | `true` deletes old `rg-opscopilot-a-*` (SubA) and `rg-opscopilot-b-*` (SubB) before deploying |

---

## OIDC Setup (GitHub Actions)

Two service principals are required — one per subscription.

### 1. Create App Registrations

```bash
# Platform (SubA)
az ad app create --display-name "sp-opscopilot-deploy-platform"
# Note appId → AZURE_CLIENT_ID_PLATFORM

# AI (SubB)
az ad app create --display-name "sp-opscopilot-deploy-ai"
# Note appId → AZURE_CLIENT_ID_AI
```

### 2. Create Service Principals

```bash
az ad sp create --id <APP_ID_PLATFORM>
az ad sp create --id <APP_ID_AI>
```

### 3. Add Federated Credentials (OIDC)

```bash
# For main branch (dev/sandbox)
az ad app federated-credential create --id <APP_ID> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# For production environment approval gate
az ad app federated-credential create --id <APP_ID> --parameters '{
  "name": "github-prod-env",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:environment:prod",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

### 4. Assign Roles

```bash
# Platform SP on SubA
az role assignment create --assignee <APP_ID_PLATFORM> \
  --role Contributor \
  --scope /subscriptions/b20a7294-6951-4107-88df-d7d320218670

az role assignment create --assignee <APP_ID_PLATFORM> \
  --role "Cost Management Contributor" \
  --scope /subscriptions/b20a7294-6951-4107-88df-d7d320218670

# AI SP on SubB
az role assignment create --assignee <APP_ID_AI> \
  --role Contributor \
  --scope /subscriptions/bd27a79c-de25-4097-a874-3bb35f2b926a

az role assignment create --assignee <APP_ID_AI> \
  --role "Cost Management Contributor" \
  --scope /subscriptions/bd27a79c-de25-4097-a874-3bb35f2b926a
```

### 5. Required GitHub Secrets

| Secret | Value |
|---|---|
| `AZURE_TENANT_ID` | `4a72b866-99a4-4388-b881-cef9c8480b1c` |
| `AZURE_CLIENT_ID_PLATFORM` | App Registration `appId` for SubA |
| `AZURE_SUBSCRIPTION_ID_PLATFORM` | `b20a7294-6951-4107-88df-d7d320218670` |
| `AZURE_CLIENT_ID_AI` | App Registration `appId` for SubB |
| `AZURE_SUBSCRIPTION_ID_AI` | `bd27a79c-de25-4097-a874-3bb35f2b926a` |
| `SQL_ADMIN_PASSWORD_PLATFORM` | Strong password for SQL admin (min 12 chars, mixed) |

---

## Running Locally

### Prerequisites
- Azure CLI ≥ 2.56
- Bicep CLI: `az bicep install`
- Contributor + Cost Management Contributor on both subscriptions

### What-If (Platform — SubA)

```bash
az login
az account set --subscription b20a7294-6951-4107-88df-d7d320218670

az deployment sub what-if \
  --location uksouth \
  --template-file azure-infra/bicep/main.platform.bicep \
  --parameters azure-infra/bicep/env/dev/platform.parameters.json \
  --parameters sqlAdminPassword="<your-password>"
```

### Deploy (Platform — SubA)

```bash
az deployment sub create \
  --location uksouth \
  --template-file azure-infra/bicep/main.platform.bicep \
  --parameters azure-infra/bicep/env/dev/platform.parameters.json \
  --parameters sqlAdminPassword="<your-password>"
```

### What-If (AI — SubB)

```bash
az account set --subscription bd27a79c-de25-4097-a874-3bb35f2b926a

az deployment sub what-if \
  --location uksouth \
  --template-file azure-infra/bicep/main.ai.bicep \
  --parameters azure-infra/bicep/env/dev/ai.parameters.json
```

### Deploy (AI — SubB)

```bash
az deployment sub create \
  --location uksouth \
  --template-file azure-infra/bicep/main.ai.bicep \
  --parameters azure-infra/bicep/env/dev/ai.parameters.json
```

### Cleanup old RGs (local)

```bash
# PowerShell
.\azure-infra\scripts\cleanup.ps1 `
  -Env dev `
  -SubscriptionA b20a7294-6951-4107-88df-d7d320218670 `
  -SubscriptionB bd27a79c-de25-4097-a874-3bb35f2b926a `
  -Target both `
  -DryRun

# Bash
chmod +x azure-infra/scripts/cleanup.sh
azure-infra/scripts/cleanup.sh \
  --env dev \
  --subscription-a b20a7294-6951-4107-88df-d7d320218670 \
  --subscription-b bd27a79c-de25-4097-a874-3bb35f2b926a \
  --target both \
  --dry-run
```

Remove `--dry-run` / `-DryRun` to execute deletions.

---

## AOAI Model Deployments

Model deployments are **intentionally excluded from Bicep**. This avoids:
- Quota/capacity errors during `what-if`
- Bicep idempotency failures on model version skew
- Accidental deployment in dev

To enable in a given environment, set `deploymentsEnabled: true` in the parameter file.  
The pipeline will then run `az cognitiveservices account deployment create` for:
- `gpt-4o-mini` (30k tokens/min)
- `text-embedding-3-small` (120k tokens/min)

The deployed AOAI key and endpoint are stored in the AI Key Vault automatically.

---

## Prod Differences

| Setting | Dev/Sandbox | Prod |
|---|---|---|
| LAW retention | 30 days | 90 days |
| LAW daily quota | 0.1 GB | Unlimited (-1) |
| Budget | £80/mo | £100/mo |
| AI Search | OFF / free SKU | ON / basic SKU |
| Container App replicas | max 1–2 | max 3–5 |
| GitHub environment gate | None | `prod` (requires reviewer approval) |

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `AuthorizationFailed` on budget | Missing Cost Management Contributor | Assign role to SP |
| `kvName already exists` | Key Vault names are globally unique | Change `keyVaultName` in parameter file |
| `StorageAccountAlreadyExists` | Storage account names globally unique | Change `storageNamePrefix` |
| `QuotaExceeded` on AOAI | Model deployment quota not available | Request quota or change region |
| OIDC login `short-lived token` | Subject mismatch in federated credential | Verify branch/environment subject exactly |
| `InvalidTemplateDeployment` scope | main.*.bicep used with wrong scope command | Use `az deployment sub create`, never `deployment group create` |
| LAW daily quota reached | 0.1 GB/day is very tight | Temporarily raise `dailyQuotaGb` in parameter file |
| Qdrant fails to mount volume | Storage account not yet provisioned | Ensure `enableStorage: true` and re-run |
| `ForbiddenByRbac` writing KV secret | Deploying SP has no KV role | Ensure `deployerObjectId` is passed (workflow resolves automatically) |
| `MissingSubscriptionRegistration` | Resource provider not registered | Re-run workflow — provider registration step runs before deploy |
| Role assignment already exists | Re-deploy hit existing RBAC assignment | Harmless — deterministic GUID names make assignments idempotent |


---

## Managed Identities & RBAC

### Container App identities

All three platform Container Apps (`apihost`, `workerhost`, `mcphost`) are deployed with
**system-assigned managed identities** baked into the Bicep definition:

```bicep
identity: {
  type: 'SystemAssigned'
}
```

This means:
- Azure creates the identity automatically on first deploy.
- Re-deploying **never** reverts the identity back to `None`.
- The `principalId` is available as a Bicep output (`caApiHost.outputs.principalId`, etc.)
  and as deployment outputs (`apiHostPrincipalId`, `workerHostPrincipalId`, `mcpHostPrincipalId`).

### RBAC assignments (IaC-managed)

Role assignments are created by **`modules/containerAppRbac.bicep`**, called from
`main.platform.bicep` after all Container Apps and the Key Vault / LAW are provisioned.

| App | Scope | Role | Role ID |
|---|---|---|---|
| apihost | Key Vault | Key Vault Secrets User | `4633458b-17de-408a-b874-0445c86b69e0` |
| workerhost | Key Vault | Key Vault Secrets User | `4633458b-17de-408a-b874-0445c86b69e0` |
| mcphost | Key Vault | Key Vault Secrets User | `4633458b-17de-408a-b874-0445c86b69e0` |
| apihost | Log Analytics Workspace | Log Analytics Reader | `73c42c96-874c-492b-b04d-ab87d138a893` |
| mcphost | Log Analytics Workspace | Log Analytics Reader | `73c42c96-874c-492b-b04d-ab87d138a893` |

> `workerhost` does not receive Log Analytics Reader because it is a background processing
> app that writes to LAW via telemetry SDKs but does not query it directly.

**Why Key Vault RBAC (not access policies)?**  
The Key Vault is deployed with `enableRbacAuthorization: true`. Azure RBAC is the recommended
model — role assignments are auditable, governed by Azure Policy, and consistent with the
rest of the platform. Access policies are legacy and are not used anywhere in this codebase.

**Idempotency**  
Role assignment names are deterministic GUIDs computed as:
```
guid(scopeResourceId, principalId, roleDefinitionId)
```
Re-running the deployment will not fail if the assignment already exists — ARM will simply
leave it untouched.

### Deploying-SP Key Vault access

The CI service principal also receives **Key Vault Secrets Officer** during deployment
(granted by `keyVault.bicep` via the `deployerObjectId` parameter). This allows the
pipeline to write secrets (SQL connection string, AOAI key/endpoint) immediately after
provisioning without a separate manual step.

---

## Provider Registration

The pipeline pre-registers all required Azure resource providers in both subscriptions
**before** running `az deployment sub create`. This prevents `MissingSubscriptionRegistration`
errors on clean subscriptions or after subscription transfers.

### Platform (SubA) providers

| Provider | Reason |
|---|---|
| `Microsoft.App` | Container Apps |
| `Microsoft.OperationalInsights` | Log Analytics Workspace |
| `Microsoft.Insights` | Application Insights |
| `Microsoft.KeyVault` | Key Vault |
| `Microsoft.Storage` | Storage Account (Qdrant volume) |
| `Microsoft.Sql` | Azure SQL Server + Database |
| `Microsoft.ManagedIdentity` | System-assigned identities on Container Apps |
| `Microsoft.Authorization` | Role assignments (RBAC) |

### AI (SubB) providers

| Provider | Reason |
|---|---|
| `Microsoft.CognitiveServices` | Azure OpenAI account |
| `Microsoft.Search` | Azure AI Search (optional) |
| `Microsoft.OperationalInsights` | Log Analytics Workspace |
| `Microsoft.Insights` | Application Insights |
| `Microsoft.KeyVault` | Key Vault |
| `Microsoft.ManagedIdentity` | Future MI use in AI sub |
| `Microsoft.Authorization` | Role assignments (RBAC) |

The `az provider register --wait` flag blocks until the provider reaches `Registered` state.
For already-registered providers this takes < 5 seconds. Only on brand-new subscriptions
will this add meaningful time (~30–90 seconds per provider).

---


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
