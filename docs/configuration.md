# Configuration Reference

All configuration is in `appsettings.json` / `appsettings.Development.json` at the host level. User secrets and environment variables override file values.

> See [getting-started.md](getting-started.md) for how to apply these settings when starting the application.

---

## ApiHost Core Settings

| Key | Type | Required | Description |
|---|---|:---:|---|
| `WORKSPACE_ID` | `string` (GUID) | Yes | Log Analytics workspace ID |
| `SQL_CONNECTION_STRING` | `string` | Yes | EF Core database connection (set via user-secrets) |
| `KeyVault:VaultUri` | `string` | No | Azure Key Vault URI (if secrets sourced from KeyVault) |
| `McpKql:Endpoint` | `string` | No | Override MCP KQL endpoint URL |
| `McpKql:ApiKey` | `string` | No | API key for MCP KQL endpoint (set via user-secrets) |

---

## Governance Settings

| Key | Type | Default | Description |
|---|---|---|---|
| `Governance:Defaults:AllowedTools` | `string[]` | `["kql_query","get_runbook"]` | Tools permitted by default for all tenants |
| `Governance:TriageEnabled` | `bool` | `true` | Enable the triage pipeline |
| `Governance:TokenBudget` | `int` | `4000` | Max LLM tokens per triage session |
| `Governance:SessionTtlMinutes` | `int` | `30` | Session expiry in minutes |
| `Governance:TenantOverrides` | `dict<string, TenantPolicy>` | `{}` | Per-tenant policy overrides (see below) |

**TenantPolicy shape**:
```jsonc
"TenantOverrides": {
  "<tenant-guid>": {
    "AllowedTools": ["kql_query", "restart_pod"],
    "TokenBudget": 8000
  }
}
```

---

## SafeActions Settings

| Key | Type | Default | Description |
|---|---|---|---|
| `SafeActions:EnableExecution` | `bool` | `false` | Master execution switch; `false` = Mode A |
| `SafeActions:EnableRealHttpProbe` | `bool` | `false` | Allow live HTTP probes |
| `SafeActions:EnableAzureReadExecutions` | `bool` | `false` | Allow Azure Resource Graph read queries |
| `SafeActions:EnableAzureMonitorReadExecutions` | `bool` | `false` | Allow Azure Monitor / Log Analytics queries |
| `SafeActions:HttpProbeTimeoutMs` | `int` | `5000` | HTTP probe timeout in milliseconds |
| `SafeActions:HttpProbeMaxResponseBytes` | `int` | `20480` | Maximum bytes read from an HTTP probe response |
| `SafeActions:AzureReadTimeoutMs` | `int` | `5000` | Timeout for Azure read operations in milliseconds |
| `SafeActions:AllowedAzureSubscriptionIds` | `string[]` | `[]` | Subscription allow-list for Azure reads |
| `SafeActions:AllowedLogAnalyticsWorkspaceIds` | `string[]` | `[]` | Workspace allow-list for Monitor queries |
| `SafeActions:AllowedExecutionTenants` | `dict<string, bool>` | `{}` | Tenants permitted to trigger write-path actions |
| `SafeActions:AllowActorHeaderFallback` | `bool` | `false` | Accept `X-Actor` header when JWT actor claim is absent |
| `SafeActions:AllowAnonymousActorFallback` | `bool` | `false` | Allow anonymous actor identity (dev only) |
| `SafeActions:EnableExecutionThrottling` | `bool` | `true` | Enable per-tenant execution rate limiter |
| `SafeActions:ExecutionThrottleWindowSeconds` | `int` | `60` | Sliding window for throttle measurement |
| `SafeActions:ExecutionThrottleMaxAttemptsPerWindow` | `int` | `10` | Max executions per tenant per window |

### Action Risk Tiers

| Action Type | Risk Level | Requires `EnableExecution` | Notes |
|---|---|---|---|
| `restart_pod` | **High** | Yes | Requires explicit operator approval |
| `http_probe` | Low | Yes | Read-only probe; controlled by `EnableRealHttpProbe` |
| `dry_run` | Low | No | Always permitted; no external calls |
| `azure_resource_get` | Medium | Yes | Azure Resource Graph read |
| `azure_monitor_query` | Medium | Yes | Log Analytics KQL read |

---

## McpHost Settings

| Key | Type | Default | Description |
|---|---|---|---|
| `AzureAuth:Mode` | `string` | `"AzureCli"` | Auth mode: `AzureCli`, `AzurePowerShell`, `ManagedIdentity` |
| `AzureAuth:TenantId` | `string` | — | Azure tenant ID for Azure credential |
| `AzureAuth:UseAzureCliCredential` | `bool` | `true` | Enable Azure CLI credential in DefaultAzureCredential chain |
| `AzureAuth:UseAzurePowerShellCredential` | `bool` | `false` | Enable Azure PowerShell credential |

---

## AgentRuns Settings

| Key | Type | Default | Description |
|---|---|---|---|
| `AgentRuns:SessionStore:Provider` | `string` | `"InMemory"` | Session store backend: `InMemory`, `Sql`, `Redis` |
| `AgentRuns:SessionStore:ConnectionString` | `string` | — | Connection string when `Provider` is `Sql` or `Redis` |

---

## Configuration Hierarchy

Values are resolved in this order (last wins):

```
appsettings.json
  → appsettings.{Environment}.json
    → Environment variables (e.g. SafeActions__EnableExecution=true)
      → User secrets (dev only)
        → Azure Key Vault (if configured)
```

---

## Further Reading

| Resource | Description |
|---|---|
| [local-dev-secrets.md](local-dev-secrets.md) | How to set user-secrets and integrate Key Vault |
| [local-dev-auth.md](local-dev-auth.md) | Azure credential provider selection |
| [governance.md](governance.md) | Governance policy design and tenant isolation |
| [getting-started.md](getting-started.md) | Quick start guide for each deployment mode |
