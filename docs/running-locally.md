# Running OpsCopilot Locally

> **Goal**: clone, build, and run all three hosts on your workstation in
> under 10 minutes.

---

## Table of contents

1. [Prerequisites](#prerequisites)
2. [Clone & build](#clone--build)
3. [Configure secrets](#configure-secrets)
4. [Deployment modes at a glance](#deployment-modes-at-a-glance)
5. [Mode A — Local Dev (default)](#mode-a--local-dev-default)
6. [Mode B — Azure Read-Only](#mode-b--azure-read-only)
7. [Mode C — Controlled Execution](#mode-c--controlled-execution)
8. [Running McpHost](#running-mcphost)
9. [Session store options](#session-store-options)
10. [Configuration loading order](#configuration-loading-order)
11. [Running the test suite](#running-the-test-suite)
12. [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Tool | Minimum version | Install |
|------|:--------------:|---------|
| .NET SDK | **10.0** | <https://dot.net/download> |
| SQL Server LocalDB | any | Ships with Visual Studio, or `winget install Microsoft.SQLServer.2022.Express` |
| Azure CLI | 2.60+ | Only needed for Mode B/C — `winget install Microsoft.AzureCLI` |
| Git | 2.40+ | <https://git-scm.com> |

> **Optional**: Azure PowerShell (`Az` module) if you prefer it over
> Azure CLI for Mode B/C authentication.

---

## Clone & build

```bash
git clone https://github.com/<org>/ops-copilot-platform.git
cd ops-copilot-platform
dotnet restore OpsCopilot.sln
dotnet build   OpsCopilot.sln --configuration Debug
```

The solution will produce three host binaries:

| Host | Project | Purpose |
|------|---------|---------|
| **ApiHost** | `src/Hosts/OpsCopilot.ApiHost` | HTTP / Minimal-API surface |
| **McpHost** | `src/Hosts/OpsCopilot.McpHost` | MCP-over-stdio KQL bridge |
| **WorkerHost** | `src/Hosts/OpsCopilot.WorkerHost` | Background job processing |

---

## Configure secrets

Two secrets are **required** for full functionality:

| Secret | Description |
|--------|-------------|
| `WORKSPACE_ID` | Azure Log Analytics workspace GUID |
| `SQL_CONNECTION_STRING` | SQL Server connection string |

Set them via User Secrets so they never touch the repository:

```powershell
cd src/Hosts/OpsCopilot.ApiHost

dotnet user-secrets set "WORKSPACE_ID" "<your-workspace-guid>"
dotnet user-secrets set "SQL_CONNECTION_STRING" \
  "Server=(localdb)\mssqllocaldb;Database=OpsCopilot;Trusted_Connection=True;MultipleActiveResultSets=true"
```

> For the full secrets guide — verification, optional overrides, and
> Key Vault setup — see [local-dev-secrets.md](local-dev-secrets.md).

---

## Deployment modes at a glance

| Mode | What is ON | What is OFF | Typical use |
|------|-----------|------------|-------------|
| **A — Local Dev** | Triage, Evaluation, Reporting | All execution, Azure reads | Day-to-day coding |
| **B — Azure Read-Only** | A + Azure Monitor queries, HTTP probes | Mutation actions (`restart_pod`) | Integration testing |
| **C — Controlled Execution** | Everything | Nothing (all guards active) | Pre-production validation |

All three modes share the same binary — behaviour is controlled entirely
through configuration.

---

## Mode A — Local Dev (default)

No extra configuration needed. `appsettings.Development.json` ships with
all execution flags set to `false`:

```bash
dotnet run --project src/Hosts/OpsCopilot.ApiHost
```

Startup diagnostics will confirm:

```
[Startup] SafeActions EnableExecution=False
```

Endpoints available at `http://localhost:5000` (or the port in
`Properties/launchSettings.json`).

### Quick smoke test

```bash
curl http://localhost:5000/healthz
# → "healthy"

curl -X POST http://localhost:5000/ingest/alert \
  -H "Content-Type: application/json" \
  -d '{"title":"test","severity":"Sev3","source":"manual"}'
```

---

## Mode B — Azure Read-Only

Mode B enables read-only Azure interactions (KQL queries via McpHost,
HTTP probes, Azure Monitor reads) while keeping mutation actions off.

### 1. Authenticate to Azure

Follow the step-by-step instructions in
[local-dev-auth.md](local-dev-auth.md) to log in via Azure CLI or
PowerShell.

### 2. Enable read flags

Override the following in User Secrets or environment variables:

```powershell
cd src/Hosts/OpsCopilot.ApiHost

dotnet user-secrets set "SafeActions:EnableRealHttpProbe"              "true"
dotnet user-secrets set "SafeActions:EnableAzureReadExecutions"        "true"
dotnet user-secrets set "SafeActions:EnableAzureMonitorReadExecutions" "true"
```

### 3. Scope access (recommended)

Restrict which Azure resources the local instance can reach:

```powershell
dotnet user-secrets set "SafeActions:AllowedAzureSubscriptionIds:0"        "<sub-id>"
dotnet user-secrets set "SafeActions:AllowedLogAnalyticsWorkspaceIds:0"    "<workspace-id>"
```

### 4. Run

```bash
dotnet run --project src/Hosts/OpsCopilot.ApiHost
```

Startup diagnostics will now show:

```
[Startup] SafeActions EnableExecution=False
```

> `EnableExecution` remains `false` — only read-path flags are on.
> Action types `http_probe`, `azure_resource_get`, and
> `azure_monitor_query` will work; `restart_pod` will not.

---

## Mode C — Controlled Execution

Mode C activates the full execution pipeline including mutation actions.
**Use with caution** — the execution guard chain (governance, tenant
policy, throttle, idempotency) is your only safety net.

### 1. Authenticate to Azure

Same as Mode B — see [local-dev-auth.md](local-dev-auth.md).

### 2. Enable execution

```powershell
cd src/Hosts/OpsCopilot.ApiHost

dotnet user-secrets set "SafeActions:EnableExecution" "true"
dotnet user-secrets set "SafeActions:EnableRealHttpProbe" "true"
dotnet user-secrets set "SafeActions:EnableAzureReadExecutions" "true"
dotnet user-secrets set "SafeActions:EnableAzureMonitorReadExecutions" "true"
```

### 3. Configure tenant allowlists

Execution is denied unless the calling tenant appears in
`AllowedExecutionTenants`:

```powershell
dotnet user-secrets set "SafeActions:AllowedExecutionTenants:tenant-abc" "true"
```

### 4. Enable throttle (recommended)

```powershell
dotnet user-secrets set "SafeActions:EnableExecutionThrottling" "true"
# Defaults: 5 attempts per 60-second window
```

### 5. Run

```bash
dotnet run --project src/Hosts/OpsCopilot.ApiHost
```

Startup log:

```
[Startup] SafeActions EnableExecution=True
```

---

## Running McpHost

McpHost is the MCP-over-stdio bridge that serves KQL queries.
ApiHost launches it as a child process using the configured command.

### Default (dev)

`appsettings.Development.json` ships with:

```json
{
  "McpKql": {
    "ServerCommand": "dotnet run --project src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj",
    "TimeoutSeconds": 90
  }
}
```

ApiHost will spawn McpHost automatically on the first KQL call.

### Override the command

If you have a pre-published McpHost binary:

```powershell
dotnet user-secrets set "McpKql:ServerCommand" "dotnet /path/to/OpsCopilot.McpHost.dll"
```

### Backward-compatible environment variables

For CI or container scenarios, the legacy env-var names still work:

| Env var | Maps to |
|---------|---------|
| `MCP_KQL_SERVER_COMMAND` | `McpKql:ServerCommand` |
| `MCP_KQL_TIMEOUT_SECONDS` | `McpKql:TimeoutSeconds` |

---

## Session store options

AgentRuns sessions are stored in-memory by default. For persistence
across restarts, switch to Redis:

| Provider | Config | Notes |
|----------|--------|-------|
| **InMemory** (default) | `AgentRuns:SessionStore:Provider = "InMemory"` | Sessions lost on restart |
| **Redis** | `AgentRuns:SessionStore:Provider = "Redis"` + `ConnectionString = "localhost:6379"` | Requires a running Redis instance |

Switch via User Secrets:

```powershell
dotnet user-secrets set "AgentRuns:SessionStore:Provider"         "Redis"
dotnet user-secrets set "AgentRuns:SessionStore:ConnectionString" "localhost:6379"
```

---

## Configuration loading order

Configuration sources are merged in this order (last wins):

| Priority | Source | Committed? |
|:--------:|--------|:----------:|
| 1 | `appsettings.json` | Yes |
| 2 | `appsettings.{Environment}.json` | Yes |
| 3 | User Secrets (Development only) | No |
| 4 | Environment Variables | No |
| 5 | Azure Key Vault (when `KeyVault:VaultUri` is set) | No |

> For deeper detail on each source see
> [local-dev-secrets.md](local-dev-secrets.md).

---

## Running the test suite

```bash
# All 618+ tests
dotnet test OpsCopilot.sln

# A specific module
dotnet test tests/Modules/SafeActions

# Integration tests (may require SQL)
dotnet test tests/Integration/OpsCopilot.Integration.Tests

# MCP contract tests
dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
```

> Tests run against in-memory / SQLite providers by default and do
> **not** require Azure credentials.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `WORKSPACE_ID is *** MISSING ***` at startup | Secret not set | `dotnet user-secrets set "WORKSPACE_ID" "<guid>"` |
| `SQL_CONNECTION_STRING is *** MISSING ***` | Secret not set | See [local-dev-secrets.md](local-dev-secrets.md) |
| `McpKql ServerCommand=(built-in default)` | Config not found | Ensure `appsettings.Development.json` exists and is valid JSON |
| Port already in use | Another process or host instance | Change port in `Properties/launchSettings.json` or `--urls` flag |
| `AzureCliCredential authentication failed` | CLI session expired | `az login --tenant <tid>` — see [local-dev-auth.md](local-dev-auth.md) |
| `PolicyDeniedException: governance_tool_denied` | Tool not in tenant allow-list | Add tool to `Governance:TenantOverrides:<tid>:AllowedTools` |
| `PolicyDeniedException: tenant_not_authorized_for_action` | Tenant missing from `AllowedExecutionTenants` | `dotnet user-secrets set "SafeActions:AllowedExecutionTenants:<tid>" "true"` |
| EF Core migration errors | LocalDB not running | Start via `sqllocaldb start MSSQLLocalDB` |
| `timeout` on McpHost calls | McpHost taking too long | Increase `McpKql:TimeoutSeconds` |

---

## Further reading

| Topic | Link |
|-------|------|
| Azure authentication deep-dive | [local-dev-auth.md](local-dev-auth.md) |
| Secrets & Key Vault setup | [local-dev-secrets.md](local-dev-secrets.md) |
| Architecture overview | [architecture.md](architecture.md) |
| Governance resolution | [governance.md](governance.md) |
| Security & threat model | [threat-model.md](threat-model.md) |
| Deploying on Azure | [deploying-on-azure.md](deploying-on-azure.md) |

---

> **License** — TBD — license has not yet been decided; see the
> repository root for updates.
