# Deploying OpsCopilot on Azure

> **Audience**: Platform engineers deploying OpsCopilot into an Azure subscription.
> For local-development setup see [running-locally.md](running-locally.md).

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Architecture Overview](#2-architecture-overview)
3. [Bicep Module Inventory](#3-bicep-module-inventory)
4. [Parameter Files](#4-parameter-files)
5. [Deploying with the Azure CLI](#5-deploying-with-the-azure-cli)
6. [CI / CD Pipeline (GitHub Actions)](#6-ci--cd-pipeline-github-actions)
7. [Key Vault Integration](#7-key-vault-integration)
8. [Foundry / Azure AI Provisioning](#8-foundry--azure-ai-provisioning)
9. [Container Apps Hosting](#9-container-apps-hosting)
10. [Deployment Modes in Production](#10-deployment-modes-in-production)
11. [Production Hardening Checklist](#11-production-hardening-checklist)
12. [Further Reading](#12-further-reading)

---

## 1. Prerequisites

| Requirement              | Minimum Version | Notes                                          |
| ------------------------ | --------------- | ---------------------------------------------- |
| Azure subscription       | —               | Owner or Contributor + RBAC Administrator       |
| Azure CLI (`az`)         | 2.60+           | `az upgrade` to update                          |
| .NET SDK                 | 10.0            | Must match `global.json`                        |
| GitHub repo access       | —               | For CI/CD with OIDC federated credentials       |
| jq                       | 1.6+            | Used by pipeline parameter-parsing steps        |

---

## 2. Architecture Overview

The infrastructure is defined in **Bicep** under `azure-infra/bicep/`.
The orchestrator (`main.bicep`) is a **subscription-scope** deployment that
creates a resource group, then deploys child modules into it.

```text
Subscription
 └─ Resource Group  (rg-opscopilot-<tenant>-<env>-<region>)
     ├─ Log Analytics Workspace
     ├─ Application Insights         (optional)
     ├─ Key Vault                    (optional)
     ├─ Storage Account              (optional)
     ├─ Azure AI / Foundry Account   (optional)
     ├─ Azure SQL
     ├─ Container Apps Environment
     ├─ Container App (ApiHost)
     ├─ Container App RBAC
     ├─ Azure OpenAI (AOAI)
     ├─ Qdrant (vector store)
     └─ AI Search
 └─ Budget  (subscription-scope)
```

> **Note**: Not every module is deployed every time.
> Boolean parameters (`enableAppInsights`, `enableKeyVault`, `enableStorage`,
> `foundryProvision`) act as feature flags.

---

## 3. Bicep Module Inventory

All modules live in `azure-infra/bicep/modules/`:

| Module                    | Purpose                                             |
| ------------------------- | --------------------------------------------------- |
| `rg.bicep`               | Resource group with tags                            |
| `logAnalytics.bicep`     | Log Analytics workspace (retention + quota)         |
| `appInsights.bicep`      | Application Insights (linked to LAW)                |
| `keyVault.bicep`         | Azure Key Vault for secrets                         |
| `storage.bicep`          | Storage account (deterministic naming)              |
| `budget.bicep`           | Subscription-scope budget with email alerts         |
| `foundry.bicep`          | Azure AI / Cognitive Services account               |
| `aoai.bicep`             | Azure OpenAI resource                               |
| `sql.bicep`              | Azure SQL Database                                  |
| `containerAppsEnv.bicep` | Container Apps Environment                          |
| `containerApp.bicep`     | Container App deployment                            |
| `containerAppRbac.bicep` | RBAC assignments for Container App managed identity |
| `qdrant.bicep`           | Qdrant vector-store sidecar / resource              |
| `search.bicep`           | Azure AI Search                                     |
| `tags.bicep`             | Common tag generation (reference template)          |

---

## 4. Parameter Files

Parameter files are organized by environment and tenant:

```
azure-infra/bicep/env/
  ├─ dev/
  │   ├─ tenantA.parameters.json
  │   ├─ tenantB.parameters.json
  │   ├─ platform.parameters.json
  │   └─ ai.parameters.json
  ├─ sandbox/
  │   ├─ tenantA.parameters.json
  │   ├─ tenantB.parameters.json
  │   ├─ platform.parameters.json
  │   └─ ai.parameters.json
  └─ prod/
      └─ …
```

Each tenant parameter file supplies every value the orchestrator needs.
Example excerpt from `env/dev/tenantA.parameters.json`:

```jsonc
{
  "parameters": {
    "environment":     { "value": "dev" },
    "tenantLabel":     { "value": "TenantA" },
    "rgName":          { "value": "rg-opscopilot-a-dev-uks" },
    "lawName":         { "value": "law-opscopilot-a-dev-uks" },
    "retentionInDays": { "value": 30 },
    "enableAppInsights": { "value": true },
    "appInsightsName":   { "value": "appi-opscopilot-a-dev-uks" },
    "enableKeyVault":  { "value": true },
    "keyVaultName":    { "value": "kv-opscopilot-a-dev-uks" },
    "budgetAmount":    { "value": 80 },
    "budgetEmails":    { "value": ["platform-team@example.com"] },
    "foundryProvision":   { "value": true },
    "foundryEnabled":     { "value": true },
    "foundryName":        { "value": "aoai-opscopilot-a-dev-uks" },
    "foundryProjectName": { "value": "opscopilot-a-dev" }
  }
}
```

### Key Parameters Reference

| Parameter            | Type    | Default      | Description                                   |
| -------------------- | ------- | ------------ | --------------------------------------------- |
| `environment`        | string  | —            | `dev`, `sandbox`, or `prod`                   |
| `location`           | string  | `uksouth`    | Azure region                                  |
| `tenantLabel`        | string  | —            | Logical tenant name (used in resource naming)  |
| `rgName`             | string  | —            | Target resource group name                     |
| `lawName`            | string  | —            | Log Analytics workspace name                   |
| `retentionInDays`    | int     | `30`         | LAW retention (30 – 730 days)                  |
| `dailyQuotaGb`       | int     | `0`          | LAW daily cap; 0 = unlimited                   |
| `enableAppInsights`  | bool    | `true`       | Create Application Insights                    |
| `enableKeyVault`     | bool    | `true`       | Create Key Vault                               |
| `enableStorage`      | bool    | `false`      | Create Storage Account                         |
| `storageSku`         | string  | `Standard_LRS` | Only `Standard_LRS` allowed              |
| `budgetAmount`       | int     | `80`         | Monthly budget in GBP                          |
| `foundryProvision`   | bool    | `false`      | Provision Azure AI / Cognitive Services        |
| `foundryEnabled`     | bool    | `false`      | Enable model deployment + RBAC                 |
| `commonTags`         | object  | `{}`         | Merged with auto-generated tags                |

---

## 5. Deploying with the Azure CLI

### One-Time Setup

```bash
# Login
az login

# Set the target subscription
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
```

### Deploying a Tenant

```bash
az deployment sub create \
  --location uksouth \
  --template-file azure-infra/bicep/main.bicep \
  --parameters @azure-infra/bicep/env/dev/tenantA.parameters.json \
  --parameters location=uksouth environment=dev
```

Replace `dev/tenantA` with the desired environment and tenant.

### Validating Before Deploying

```bash
az deployment sub validate \
  --location uksouth \
  --template-file azure-infra/bicep/main.bicep \
  --parameters @azure-infra/bicep/env/dev/tenantA.parameters.json
```

### What-If Preview

```bash
az deployment sub what-if \
  --location uksouth \
  --template-file azure-infra/bicep/main.bicep \
  --parameters @azure-infra/bicep/env/dev/tenantA.parameters.json
```

---

## 6. CI / CD Pipeline (GitHub Actions)

The deployment pipeline lives at `infrastructure/pipelines/deploy-infra.yml`.

### Trigger Conditions

| Trigger            | Condition                                         |
| ------------------ | ------------------------------------------------- |
| `push` to `main`   | Only when files under `azure-infra/**` change      |
| `workflow_dispatch` | Manual trigger with `dev` or `sandbox` choice      |

### Pipeline Jobs

```text
deploy-base  ──▶  foundry (conditional)
```

1. **deploy-base** — resolves the parameter file, parses Foundry settings via
   `jq`, logs in with **OIDC** (`azure/login@v2`), then runs
   `az deployment group create` with the resolved template and parameters.
2. **foundry** — runs only when `enableFoundry == true`. Provisions the Azure
   AI Foundry account and project via REST API, then optionally wires a
   model endpoint connection.

### Required GitHub Variables

Configure these in **Settings → Secrets and variables → Actions → Variables**:

| Variable                  | Description                          |
| ------------------------- | ------------------------------------ |
| `AZURE_CLIENT_ID`         | Service principal / app registration |
| `AZURE_TENANT_ID`         | Azure AD tenant ID                   |
| `AZURE_SUBSCRIPTION_ID`   | Target subscription                  |
| `AZURE_RG_DEV`            | Resource group name for `dev`        |
| `AZURE_RG_SANDBOX`        | Resource group name for `sandbox`    |

### OIDC Setup

The pipeline requests `id-token: write` permissions.  Configure a
[federated credential](https://learn.microsoft.com/azure/active-directory/develop/workload-identity-federation)
on the app registration so GitHub Actions can authenticate without secrets.

---

## 7. Key Vault Integration

When `enableKeyVault` is `true`, a Key Vault is created by the pipeline.
The ApiHost loads secrets at startup when `KeyVault:VaultUri` is set:

```jsonc
// appsettings.json (production)
{
  "KeyVault": {
    "VaultUri": "https://kv-opscopilot-a-dev-uks.vault.azure.net/"
  }
}
```

### Required Secrets

Store these in Key Vault (note: Key Vault uses hyphens; the runtime maps to
the colon-delimited config key automatically):

| Key Vault Secret Name       | Maps To Config Key             | Purpose                         |
| --------------------------- | ------------------------------ | ------------------------------- |
| `WORKSPACE-ID`              | `WORKSPACE_ID`                 | Log Analytics workspace GUID    |
| `SQL-CONNECTION-STRING`     | `ConnectionStrings:Sql`        | Azure SQL connection string     |

### RBAC for Managed Identity

The Container App's system-assigned managed identity needs:

```bash
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee "<CONTAINER_APP_MANAGED_IDENTITY_PRINCIPAL_ID>" \
  --scope "/subscriptions/<SUB_ID>/resourceGroups/<RG>/providers/Microsoft.KeyVault/vaults/<KV_NAME>"
```

---

## 8. Foundry / Azure AI Provisioning

Foundry deployment is a **two-phase** process:

1. **Bicep phase** (`foundryProvision: true`) — creates the Cognitive Services
   account resource via `modules/foundry.bicep`.
2. **CLI phase** (pipeline `foundry` job) — creates the AI Foundry project and
   optionally wires the model endpoint connection via Azure REST API.

Set the following parameters to enable Foundry:

```jsonc
{
  "foundryProvision":    { "value": true },
  "foundryEnabled":      { "value": true },
  "foundryName":         { "value": "aoai-opscopilot-a-dev-uks" },
  "foundryProjectName":  { "value": "opscopilot-a-dev" }
}
```

The pipeline `foundry` job runs only when `enableFoundry == true` and
provisions:
- The AI account via `az rest --method PUT`
- The project within the account
- An optional model connection (when `modelEndpoint` and `modelName` are set)

---

## 9. Container Apps Hosting

The Bicep modules `containerAppsEnv.bicep`, `containerApp.bicep`, and
`containerAppRbac.bicep` provision the Container Apps infrastructure.

### Typical Environment Variables for the Container App

Map the following application settings to the Container App configuration
(or inject via Key Vault references):

| Setting                             | Example Value                              |
| ----------------------------------- | ------------------------------------------ |
| `ASPNETCORE_ENVIRONMENT`            | `Production`                               |
| `KeyVault__VaultUri`                | `https://kv-opscopilot-a-dev-uks.vault.azure.net/` |
| `AzureAuth__Mode`                   | `DefaultAzureCredential`                   |
| `SafeActions__EnableExecution`       | `true`                                     |
| `Governance__Defaults__AllowedTools` | `kql_query,runbook_search`                 |
| `AgentRuns__SessionStore__Provider`  | `Redis`                                    |
| `AgentRuns__SessionStore__ConnectionString` | `<redis-connection-string>`        |

> **Important**: In production, set `AzureAuth:Mode` to
> `DefaultAzureCredential` so the managed identity is used automatically.

---

## 10. Deployment Modes in Production

| Mode | Name                 | `EnableExecution` | Read Ops Active? | Use Case                         |
| ---- | -------------------- | ----------------- | ---------------- | -------------------------------- |
| A    | Local Dev            | `false`           | No               | Local development only           |
| B    | Azure Read-Only      | `false`           | Yes              | Monitoring dashboards, probes    |
| C    | Controlled Execution | `true`            | Yes              | Full production with approvals   |

**Recommended production path**: Deploy in **Mode B** first, validate
telemetry in Application Insights, then promote to **Mode C** by setting
`SafeActions:EnableExecution = true`.

See [governance.md](governance.md) for per-tenant tool allow-lists and
budget controls.

---

## 11. Production Hardening Checklist

- [ ] **Key Vault**: All secrets stored in Key Vault, never in appsettings
- [ ] **Managed Identity**: Container App uses system-assigned identity with
      least-privilege RBAC (Key Vault Secrets User, Log Analytics Reader)
- [ ] **Network**: Container Apps Environment configured with VNet integration
      if required by organizational policy
- [ ] **TLS**: Ingress configured for HTTPS-only with a custom domain
- [ ] **Budget**: `budgetAmount` set with appropriate `budgetEmails`
- [ ] **Log retention**: `retentionInDays` set per compliance requirements
      (30–730 days)
- [ ] **Governance**: Tenant configs restrict `AllowedTools` and `TokenBudget`
      (see [governance.md](governance.md))
- [ ] **SafeActions**: Mode B validated before enabling Mode C
- [ ] **Redis**: Session store uses Redis with TLS in production
      (`AgentRuns:SessionStore:Provider = Redis`)
- [ ] **Monitoring**: Application Insights enabled, alerts configured
- [ ] **OIDC**: Pipeline uses federated credentials — no long-lived secrets

See [SECURITY.md](../SECURITY.md) for the full threat model and mitigation
matrix.

---

## 12. Further Reading

| Document                                          | Description                          |
| ------------------------------------------------- | ------------------------------------ |
| [running-locally.md](running-locally.md)          | Local development quick-start        |
| [architecture.md](architecture.md)                | Architecture deep-dive               |
| [governance.md](governance.md)                    | Tenant governance & tool allow-lists |
| [threat-model.md](threat-model.md)                | Threat model & mitigations           |
| [SECURITY.md](../SECURITY.md)                     | Security policy & reporting          |
| [local-dev-auth.md](local-dev-auth.md)            | Azure auth for developers            |
| [local-dev-secrets.md](local-dev-secrets.md)      | Secrets management                   |

---

## License

TBD — license has not yet been decided. See the root [README](../README.md) for
current status.
