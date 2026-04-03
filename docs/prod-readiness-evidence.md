# Production Readiness Evidence — OpsCopilot Platform

**Assessment date**: 2025-07  
**Branch**: `feature/opscopilot-azure-slices-next-20260321-1430`  
**Baseline score**: ~6.5 / 10 (NOT production-ready at assessment)

---

## Executive Summary

The initial production readiness assessment identified **5 blockers** and **8 important gaps** preventing safe Azure deployment. This document records every gap, the remediation applied, and the residual operator actions required before a production release is authorised.

---

## Phase 1 — CD Pipeline & Container Registry

### Gap: No Docker images / no CD pipeline

| Item | Before | After |
|------|--------|-------|
| Dockerfiles | None | Added × 3 (ApiHost, McpHost, WorkerHost) |
| `.dockerignore` | None | Added at solution root |
| CD pipeline template | None | `templates/github-actions/cd-app.yml` created |
| ACR resource | None | `azure-infra/bicep/modules/acr.bicep` created |
| AcrPull RBAC | None | `containerAppRbac.bicep` — conditional grants for all 3 managed identities |

**Files created:**
- `src/Hosts/OpsCopilot.ApiHost/Dockerfile`
- `src/Hosts/OpsCopilot.McpHost/Dockerfile`
- `src/Hosts/OpsCopilot.WorkerHost/Dockerfile`
- `.dockerignore`
- `azure-infra/bicep/modules/acr.bicep`
- `templates/github-actions/cd-app.yml`

**Docker image strategy:**
- Build stage: `mcr.microsoft.com/dotnet/sdk:10.0` (exact SDK family matching `global.json`)
- Runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0` (all 3 hosts, including WorkerHost)
- Non-root user: `USER $APP_UID` (built-in UID `1654` from .NET 8+ base images)
- Packs filesystem: `packs/` directory copied to `/app/packs/` at runtime

**Residual operator actions required:**
- [ ] Set GitHub repo variable `ACR_NAME` (e.g. `acropsctpltfprod`)
- [ ] Set GitHub repo secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID_PLATFORM`
- [ ] Create Federated Identity Credential on the CD service principal (OIDC, subject `repo:ORG/REPO:ref:refs/heads/main`)
- [ ] Grant AcrPush to the CD service principal:
  ```bash
  ACR_ID=$(az acr show --name acropsctpltfprod --query id -o tsv)
  az role assignment create --role AcrPush --assignee <SP_OBJECT_ID> --scope "$ACR_ID"
  ```
- [ ] Copy `templates/github-actions/cd-app.yml` → `.github/workflows/cd-app.yml`

---

## Phase 2 — Security Scanning in CI

### Gap: No SAST, no container vulnerability scanning

| Item | Before | After |
|------|--------|-------|
| Trivy filesystem scan | None | Added to `templates/github-actions/ci.yml` |
| CodeQL C# analysis | None | Added to `templates/github-actions/ci.yml` |
| SARIF upload to GitHub Security | None | Added (Trivy + CodeQL both upload) |
| CI permissions | `contents: read` only | Added `security-events: write` |

**Scanning config:**
- Trivy: `severity: HIGH,CRITICAL`, `exit-code: 1` (fails build), `ignore-unfixed: true`
- CodeQL: `build-mode: none` (re-uses compiled artifacts from the Build step)

**Residual operator actions required:**
- [ ] Copy `templates/github-actions/ci.yml` → `.github/workflows/ci.yml`
- [ ] Ensure GitHub Advanced Security is enabled on the repository (required for CodeQL SARIF upload)

---

## Phase 3 — Infrastructure Completion

### Gap 3a: Health probes missing from Container App Bicep

| Item | Before | After |
|------|--------|-------|
| Liveness probe | None | Added: HTTP GET `/healthz/live`, 10s delay, 10s period, 3 failures |
| Readiness probe | None | Added: HTTP GET `/healthz/ready`, 5s delay, 5s period, 3 failures |
| WorkerHost probes | N/A | `enableHealthProbes: false` (no ingress — probes would fail) |

### Gap 3b: Environment variables not injected into Container Apps

| Variable | Source | Targets |
|----------|--------|---------|
| `KeyVault__VaultUri` | `kv.outputs.keyVaultUri` | All 3 |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `appInsights.outputs.connectionString` | All 3 |
| `WORKSPACE_ID` | `law.outputs.customerId` (GUID) | All 3 |
| `ASPNETCORE_ENVIRONMENT` | `environment == 'prod' ? 'Production' : 'Development'` | All 3 |
| `Authentication__Entra__TenantId` | `entraTenantId` param (default: `tenant().tenantId`) | All 3 |
| `Authentication__Entra__Audience` | `entraAudience` param | ApiHost + McpHost |

### Gap 3c: `minReplicas: 0` in production

| Item | Before | After |
|------|--------|-------|
| `minReplicas` | Hardcoded `0` | Parameterised; prod override = `1` |

**Files modified:**
- `azure-infra/bicep/modules/containerApp.bicep` — health probe params + conditional probe block
- `azure-infra/bicep/modules/containerAppRbac.bicep` — `acrResourceId` param + 3 AcrPull role assignments
- `azure-infra/bicep/main.platform.bicep` — new params, `commonEnvVars`/`apiEnvVars` vars, ACR module, env var injection, minReplicas param, caRbac wiring, `acrLoginServer` output
- `azure-infra/bicep/env/prod/platform.parameters.json` — `acrName`, `minReplicas: 1`, `entraAudience`

**Residual operator actions required:**
- [ ] Set `entraAudience` value in `prod/platform.parameters.json` to the actual Entra app registration audience URI (e.g. `api://opscopilot-prod`)
- [ ] Deploy updated infrastructure:
  ```bash
  az deployment sub create \
    --location uksouth \
    --template-file azure-infra/bicep/main.platform.bicep \
    --parameters @azure-infra/bicep/env/prod/platform.parameters.json
  ```

---

## Phase 4 — Staging Smoke Tests

### Gap: No post-deploy verification

| Item | Before | After |
|------|--------|-------|
| Smoke test script | None | `scripts/Invoke-SmokeTest.ps1` |
| CD pipeline smoke gate | None | Final step in `cd-app.yml` calls smoke test (skipped if `API_FQDN` var not set) |

**Smoke test coverage:**
- `GET /healthz/live` → ApiHost (HTTP 200)
- `GET /healthz/ready` → ApiHost (HTTP 200)
- `GET /healthz/live` → McpHost (HTTP 200)
- `GET /healthz/ready` → McpHost (HTTP 200)

Retries: up to 3 attempts with exponential back-off (2s, 4s).

**Residual operator actions required:**
- [ ] Set GitHub repo variable `API_FQDN` to the ApiHost FQDN (discovered post-first-deploy via `az containerapp show --query properties.configuration.ingress.fqdn`)

---

## Phase 5 — Production Parameter Fixes

### Gap: `deploymentsEnabled: false` in AI parameters

The AI subscription `ai.parameters.json` still has `deploymentsEnabled: false`. This is intentional — Azure OpenAI model deployments require verified capacity and quota approval.

**Residual operator actions required:**
- [ ] Verify Azure OpenAI quota in the target region
- [ ] Set `deploymentsEnabled: true` in `azure-infra/bicep/env/prod/ai.parameters.json` when ready
- [ ] Set `budgetAmountGbp` and `budgetAlertEmail` in cost-alert parameters

---

## Pre-Production Checklist

### Blockers (must be resolved before first production deploy)

- [ ] `entraAudience` parameter value set in `prod/platform.parameters.json`
- [ ] Federated Identity Credential configured on CD service principal
- [ ] AcrPush role granted to CD service principal
- [ ] GitHub secrets configured (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID_PLATFORM`)
- [ ] Infrastructure re-deployed with updated Bicep

### Important (should be resolved within first sprint)

- [ ] GitHub Advanced Security enabled (for CodeQL)
- [ ] `API_FQDN` repo variable set (for smoke tests)
- [ ] Azure OpenAI quota verified and `deploymentsEnabled` decision made
- [ ] Budget alert email configured
- [ ] Container App custom domain + managed certificate configured (TLS termination)
- [ ] Log Analytics retention set to ≥ 90 days for prod

---

## Build & Test Evidence

All existing tests continue to pass. No breaking changes introduced.

```
dotnet build OpsCopilot.sln -warnaserror   # required green gate
dotnet test  OpsCopilot.sln --configuration Release
```

Infrastructure changes are additive and backward-compatible:
- New Bicep params all have safe defaults (`minReplicas = 0`, `acrName = auto-generated`, `entraAudience = ""`)
- `enableHealthProbes` defaults to `true` but is gated on `enableIngress` (no regression for WorkerHost)
- AcrPull RBAC only activates when `acrResourceId` is non-empty

---

*Produced as part of the production readiness implementation pass. See CLAUDE.md for slice constraints.*
