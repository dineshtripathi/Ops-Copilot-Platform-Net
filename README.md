# Ops Copilot Platform

Ops Copilot is a modular .NET platform for operations triage, alert ingestion, governance, reporting, and safe action orchestration. It uses the **Model Context Protocol (MCP)** to query Azure Log Analytics via a dedicated MCP stdio tool server, keeping a strict boundary between the API surface and Azure SDK dependencies.

---

## Solution Layout

```
src/
  BuildingBlocks/       Shared cross-cutting libraries (Application, Contracts, Domain, Infrastructure)
  Hosts/
    OpsCopilot.ApiHost/   Main API host (HTTP, Minimal APIs)
    OpsCopilot.McpHost/   MCP tool server (stdio transport, KQL queries)
    OpsCopilot.WorkerHost/ Background processing host
  Modules/              Bounded modules (AgentRuns, AlertIngestion, Connectors, Evaluation,
                        Governance, Prompting, Rag, Reporting, SafeActions, Tenancy)
tests/                  Integration, module, and MCP contract test projects
infrastructure/         Azure deployment artifacts (Bicep)
docs/                   Developer guides & product vision
```

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | **10.0+** | `dotnet --version` to verify |
| PowerShell | 5.1+ or 7+ | Required for `AzurePowerShellCredential` |
| SQL Server LocalDB | any | Ships with Visual Studio; used for local EF Core persistence |
| Azure CLI **or** Azure PowerShell | latest | At least one is needed for Azure Log Analytics authentication |

---

## Required Configuration

The ApiHost requires two secrets that must **never** be committed to source control.  
Set them via [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) (local dev) or Azure Key Vault (deployed environments).

### 1. Set User Secrets (one-time, local dev)

```powershell
cd src/Hosts/OpsCopilot.ApiHost

# Log Analytics workspace GUID — obtain from Azure Portal → Log Analytics → Properties
dotnet user-secrets set "WORKSPACE_ID" "<your-workspace-guid>"

# SQL Server connection string — LocalDB for local development
dotnet user-secrets set "SQL_CONNECTION_STRING" "Server=(localdb)\mssqllocaldb;Database=OpsCopilot;Trusted_Connection=True;MultipleActiveResultSets=true"
```

### 2. Verify secrets are set

```powershell
dotnet user-secrets list
# Expected:
#   WORKSPACE_ID = <guid>
#   SQL_CONNECTION_STRING = Server=(localdb)\...
```

### 3. Authenticate to Azure (for KQL queries)

The McpHost authenticates to Azure Log Analytics. In Development mode it uses `ExplicitChain` credential resolution (Azure CLI + Azure PowerShell). You need at least one active session:

```powershell
# Option A — Azure CLI (recommended)
az login --tenant <YOUR_TENANT_ID>

# Option B — Azure PowerShell
Connect-AzAccount -Tenant <YOUR_TENANT_ID>
Enable-AzContextAutosave -Scope CurrentUser   # persist token for child processes
```

Your identity must have the **Log Analytics Reader** role on the target workspace.

> See [docs/local-dev-auth.md](docs/local-dev-auth.md) for full troubleshooting and credential configuration.  
> See [docs/local-dev-secrets.md](docs/local-dev-secrets.md) for Key Vault integration and production secrets.

---

## Configuration Reference

### ApiHost (`appsettings.json` / User Secrets / env vars)

| Key | Required | Default | Description |
|---|:---:|---|---|
| `WORKSPACE_ID` | **Yes** | — | Azure Log Analytics workspace GUID |
| `SQL_CONNECTION_STRING` | **Yes** | — | SQL Server connection string (EF Core persistence) |
| `McpKql:ServerCommand` | No | `dotnet run --project src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj` | Command to launch the MCP tool server |
| `McpKql:TimeoutSeconds` | No | `90` | Per-call MCP request timeout (seconds) |
| `KeyVault:VaultUri` | No | *(empty)* | Azure Key Vault URI — when set, secrets are loaded from the vault |

### McpHost (`appsettings.json` / `appsettings.Development.json`)

| Key | Required | Default (Dev) | Description |
|---|:---:|---|---|
| `AzureAuth:Mode` | No | `ExplicitChain` | `ExplicitChain` or `DefaultAzureCredential` |
| `AzureAuth:TenantId` | No | *(empty)* | Azure AD tenant GUID |
| `AzureAuth:UseAzureCliCredential` | No | `true` | Include Azure CLI in credential chain |
| `AzureAuth:UseAzurePowerShellCredential` | No | `true` | Include Azure PowerShell in credential chain |
| `AzureAuth:UseAzureDeveloperCliCredential` | No | `false` | Include `azd` CLI in credential chain |
| `AzureAuth:CredentialProcessTimeoutSeconds` | No | `60` | CLI/PS process timeout |

---

## Quick Start

### Build

```powershell
dotnet build OpsCopilot.sln
```

### Run the API Host

```powershell
# From the solution root
dotnet run --project src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj
```

The server starts at **http://localhost:5000** (configured in `launchSettings.json`).  
On first run, EF Core migrations automatically create the `OpsCopilot` database and the `agentRuns` schema in LocalDB.

Startup logs confirm configuration status:

```
[Startup] Environment: Development
[Startup] McpKql  ServerCommand=dotnet run --project ... | TimeoutSeconds=90
[Startup] Config  WORKSPACE_ID=set | SQL_CONNECTION_STRING=set
```

If either secret shows `*** MISSING ***`, check your User Secrets setup above.

### Health Check

```powershell
curl http://localhost:5000/healthz
# → "healthy"
```

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/healthz` | Liveness probe — returns `"healthy"` |
| `POST` | `/ingest/alert` | Ingests a raw alert, computes SHA-256 fingerprint, creates a `Pending` AgentRun |
| `POST` | `/agent/triage` | End-to-end KQL triage: validate → execute KQL via MCP → persist ledger entry |

### POST `/agent/triage` — Example

**Headers:**

| Header | Required | Description |
|---|:---:|---|
| `Content-Type` | Yes | `application/json` |
| `x-tenant-id` | Yes | Tenant identifier (non-empty string) |

**Request body:**

```json
{
  "alertPayload": {
    "alertSource": "Sentinel",
    "fingerprint": "fp-abc-001",
    "severity": "High",
    "description": "CPU spike on vm-web-01"
  },
  "timeRangeMinutes": 60
}
```

**Validation rules:**

| Field | Rule |
|---|---|
| `x-tenant-id` header | Must be present and non-empty |
| `alertPayload` | Must not be null |
| `alertPayload.alertSource` | Must not be null or whitespace |
| `alertPayload.fingerprint` | Must not be null or whitespace |
| `timeRangeMinutes` | Must be between 1 and 1440 (1 minute to 24 hours) |
| `workspaceId` (if supplied in body) | Must be a valid GUID |

**Successful response (HTTP 200):**

```json
{
  "runId": "2a699e71-b28c-4e53-9966-1938c249aeea",
  "status": "Completed",
  "summary": { "rowCount": 5 },
  "citations": [
    {
      "workspaceId": "6b530cc6-14bb-4fad-9577-3a349209ae1c",
      "executedQuery": "search * | where TimeGenerated > ago(60m) | take 20",
      "timespan": "PT60M",
      "executedAtUtc": "2026-02-21T18:58:37.739Z"
    }
  ]
}
```

---

## Testing

```powershell
# Run all unit & module tests
dotnet test OpsCopilot.sln

# Run a specific module's tests
dotnet test tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests
```

---

## Architecture Notes

- **MCP Hard-Boundary**: ApiHost **must not** reference `Azure.Monitor.Query` or call Log Analytics directly. All KQL observations travel through McpHost via the MCP stdio protocol (`StdioClientTransport` → `McpStdioKqlToolClient`).
- **Clean Architecture**: Each module follows Domain → Application → Infrastructure → Presentation layering. See [docs/pdd/DEPENDENCY_RULES.md](docs/pdd/DEPENDENCY_RULES.md).
- **EF Core Persistence**: The `AgentRuns` module uses its own `AgentRunsDbContext` with the `agentRuns` schema. Migrations are applied automatically on startup via `MigrateAsync()`.

---

## Infrastructure

Azure infrastructure assets are under `infrastructure/`.
See [infrastructure/README.md](infrastructure/README.md) for deployment details, cost guardrails, and optional Azure AI Foundry enablement.

---

## Further Reading

| Document | Description |
|---|---|
| [docs/PROJECT_VISION.md](docs/PROJECT_VISION.md) | Product vision and target architecture |
| [docs/local-dev-secrets.md](docs/local-dev-secrets.md) | Secrets setup, Key Vault integration |
| [docs/local-dev-auth.md](docs/local-dev-auth.md) | Azure authentication troubleshooting |
| [docs/dev-slice-1-curl.md](docs/dev-slice-1-curl.md) | Curl smoke-test guide for Slice 1 endpoints |
| [docs/pdd/DEPENDENCY_RULES.md](docs/pdd/DEPENDENCY_RULES.md) | Module dependency rules |
| [src/Hosts/OpsCopilot.McpHost/README.md](src/Hosts/OpsCopilot.McpHost/README.md) | MCP tool server documentation |

---

## Module Ownership & Status

Use this table as a living ownership and maturity tracker. Keep dependency direction aligned with `docs/pdd/DEPENDENCY_RULES.md`.

| Module | Owning Team | Tech Lead | Status | Notes |
| --- | --- | --- | --- | --- |
| AgentRuns | _TBD_ | _TBD_ | In Progress | Triage + ledger persistence |
| AlertIngestion | _TBD_ | _TBD_ | In Progress | Alert ingestion + fingerprinting |
| Connectors | _TBD_ | _TBD_ | Planned | |
| Evaluation | _TBD_ | _TBD_ | Planned | |
| Governance | _TBD_ | _TBD_ | Planned | |
| Prompting | _TBD_ | _TBD_ | Planned | |
| Rag | _TBD_ | _TBD_ | Planned | |
| Reporting | _TBD_ | _TBD_ | Planned | |
| SafeActions | _TBD_ | _TBD_ | Planned | |
| Tenancy | _TBD_ | _TBD_ | Planned | |
