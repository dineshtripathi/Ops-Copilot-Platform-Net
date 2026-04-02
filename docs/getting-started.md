# Getting Started

This guide walks you through running OpsCopilot locally in each of its three operating modes.

> **Prerequisites** — see the [Prerequisites](#prerequisites) section below before starting.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | **10.0+** | `dotnet --version` to verify |
| PowerShell | 5.1+ or 7+ | Required for `AzurePowerShellCredential` |
| SQL Server LocalDB | any | Ships with Visual Studio; used for local EF Core persistence |
| Azure CLI **or** Azure PowerShell | latest | Needed for Azure Log Analytics authentication (Modes B/C) |

---

## Deployment Modes

| Mode | Name | Description | `EnableExecution` | Real HTTP Probes | Azure Read | Azure Monitor Read |
|------|------|-------------|:-----------------:|:----------------:|:----------:|:------------------:|
| **A** | Local Dev | All execution off — triage and governance only | `false` | `false` | `false` | `false` |
| **B** | Azure Read-Only | Probes + Azure Monitor read queries enabled; no mutations | `true` | `true` | `true` | `true` |
| **C** | Controlled Execution | Full execution with approval gates and throttling | `true` | `true` | `true` | `true` |

> Mode A is the default in `appsettings.Development.json`. Transition to B or C by toggling the `SafeActions:*` flags documented in [configuration.md](configuration.md).

---

## Mode A — Local Dev (no Azure required)

```powershell
# 1. Build
dotnet build OpsCopilot.sln

# 2. Set required secrets (one-time)
cd src/Hosts/OpsCopilot.ApiHost
dotnet user-secrets set "WORKSPACE_ID" "<your-workspace-guid>"
dotnet user-secrets set "SQL_CONNECTION_STRING" "Server=(localdb)\mssqllocaldb;Database=OpsCopilot;Trusted_Connection=True;MultipleActiveResultSets=true"
cd ../../..

# 3. Run
dotnet run --project src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj

# 4. Verify
curl http://localhost:5000/healthz
# → "healthy"
```

EF Core migrations run automatically on first start, creating the `OpsCopilot` database in LocalDB.

---

## Mode B — Azure Read-Only

1. Complete Mode A setup.
2. Authenticate to Azure:
   ```powershell
   az login --tenant <YOUR_TENANT_ID>
   ```
   Your identity needs the **Log Analytics Reader** role on the target workspace.
3. In `appsettings.Development.json`, set:
   ```jsonc
   "SafeActions": {
     "EnableExecution": true,
     "EnableRealHttpProbe": true,
     "EnableAzureReadExecutions": true,
     "EnableAzureMonitorReadExecutions": true
   }
   ```
4. Restart the ApiHost.

---

## Mode C — Controlled Execution

1. Complete Mode B setup.
2. Configure tenant allow-lists and throttling:
   ```jsonc
   "SafeActions": {
     "AllowedExecutionTenants": { "<tenant-guid>": true },
     "EnableExecutionThrottling": true,
     "ExecutionThrottleWindowSeconds": 60,
     "ExecutionThrottleMaxAttemptsPerWindow": 5
   }
   ```
3. All `restart_pod` (High risk) actions require explicit approval before execution.

---

## Running Tests

```powershell
# Run all tests
dotnet test OpsCopilot.sln

# Run a specific module
dotnet test tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests

# Run evaluation scenarios via the API
curl http://localhost:5000/evaluation/run
```

The evaluation framework contains **11 deterministic scenarios** across AlertIngestion (4), SafeActions (4), and Reporting (3). These run in-process with no external dependencies.

---

## Further Reading

| Guide | Description |
|---|---|
| [local-dev-auth.md](local-dev-auth.md) | Azure credential troubleshooting |
| [local-dev-secrets.md](local-dev-secrets.md) | Secret management and Key Vault integration |
| [running-locally.md](running-locally.md) | Full local development setup guide |
| [configuration.md](configuration.md) | All `appsettings.json` keys and defaults |
| [deploying-on-azure.md](deploying-on-azure.md) | Azure deployment guide |
