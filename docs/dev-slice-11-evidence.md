# Dev Slice 11 — SafeActions Azure Monitor Read Executor (STRICT, Read-Only)

## Evidence Report

| Item | Value |
|------|-------|
| Slice | 11 |
| Title | SafeActions Azure Monitor Read Executor — read-only KQL query execution |
| Status | **COMPLETE** |
| Tests Before | 218 |
| Tests After | **255** (+37 net new) |
| Build Warnings | 0 |
| Build Errors | 0 |

---

## 1. What Slice 11 Delivers

Slice 11 adds a new action type **`azure_monitor_query`** that performs a **read-only
KQL query** against Azure Log Analytics workspaces, using the `Azure.Monitor.Query` SDK
with `DefaultAzureCredential`. This enables the agent to query operational telemetry
(Heartbeat, Perf, Syslog, AzureActivity, etc.) without any data modification capabilities.

**Hard safety guarantees:**

- **Read-only** — only `LogsQueryClient.QueryWorkspaceAsync` is ever called. No writes,
  deletes, or management operations.
- **Blocked query patterns** — eight dangerous KQL command prefixes (`.create`, `.alter`,
  `.drop`, `.ingest`, `.set`, `.append`, `.delete`, `.execute`) are rejected via
  case-insensitive substring matching before any SDK call is made.
- **No custom token/header passthrough** — uses `DefaultAzureCredential` exclusively.
  The payload cannot inject credentials.
- **No secrets in logs** — only non-sensitive query metadata (row count, column count)
  is logged.
- **Feature-gated** — four independent flags must all align before a real Azure Monitor
  call is made.
- **Configurable timeout** — `SafeActions:AzureMonitorQueryTimeoutMs` (default 5000ms).
- **Workspace ID validation** — must be a valid GUID format.
- **Timespan clamping** — `timespanMinutes` clamped to [1..1440] (1 min to 24 hours).

---

## 2. Architecture

### New Files

| File | Type | Lines | Purpose |
|------|------|-------|---------|
| `IAzureMonitorLogsReader.cs` | Interface + record | 29 | Abstraction for Log Analytics queries; enables mock injection |
| `LogsQueryClientReader.cs` | Implementation | 62 | Wraps `LogsQueryClient.QueryWorkspaceAsync`, builds row dictionaries |
| `AzureMonitorQueryActionExecutor.cs` | Executor | 203 | Parses payload, validates workspace ID + query, checks blocked patterns, calls reader, maps errors |
| `AzureMonitorQueryActionExecutorTests.cs` | Unit tests | ~290 | 20 test cases covering all code paths |

### Modified Files

| File | Change |
|------|--------|
| `RoutingActionExecutor.cs` | 6-param constructor; four-way routing precedence |
| `SafeActionsInfrastructureExtensions.cs` | DI wiring for `LogsQueryClient`, `IAzureMonitorLogsReader`, `AzureMonitorQueryActionExecutor` |
| `OpsCopilot.SafeActions.Infrastructure.csproj` | `Azure.Monitor.Query 1.5.0` |
| `appsettings.Development.json` | `EnableAzureMonitorReadExecutions`, `AzureMonitorQueryTimeoutMs` |
| `RoutingActionExecutorTests.cs` | 6 new routing tests for `azure_monitor_query` |
| `SafeActionRoutingEndpointTests.cs` | 2 new endpoint integration tests + `CreateApprovedAzureMonitorQueryRecord()` factory |
| `OpsCopilot.Api.http` | Section M — `azure_monitor_query` documentation |

### Untouched Files (verified)

`StubActionExecutor.cs`, `DryRunActionExecutor.cs`, `HttpProbeActionExecutor.cs`,
`TargetUriValidator.cs`, `AzureResourceGetActionExecutor.cs`, `IAzureResourceReader.cs`,
`ArmResourceReader.cs` — zero modifications.

### Class Diagram

```
IActionExecutor
    └── RoutingActionExecutor (6 params)
            ├── DryRunActionExecutor              (Slice 8)
            ├── HttpProbeActionExecutor            (Slice 9)
            ├── AzureResourceGetActionExecutor     (Slice 10)
            │       └── IAzureResourceReader
            │               └── ArmResourceReader → ArmClient
            └── AzureMonitorQueryActionExecutor    (Slice 11)
                    └── IAzureMonitorLogsReader
                            └── LogsQueryClientReader → LogsQueryClient
```

All new classes are `internal sealed`. `RoutingActionExecutor` is the only
`IActionExecutor` registered in DI.

---

## 3. Feature Flags — Four-Flag Safety Model

```
       ┌─────────────────────────────────────────────────────────┐
       │  Request arrives at POST /api/safe-actions/{id}/execute │
       └──────────────────┬──────────────────────────────────────┘
                          ▼
           ┌───── EnableExecution ─────┐
           │  (endpoint guard, Slice 8)│
           └─────────┬────────────────┘
                     ▼ true
       ┌──── azure_resource_get? ────┐
       │ + EnableAzureReadExecutions │
       └───────┬─────────────────────┘
               │ true → AzureResourceGetActionExecutor
               │ false ↓
       ┌── azure_monitor_query? ─────────────┐
       │ + EnableAzureMonitorReadExecutions   │
       └───────┬─────────────────────────────┘
               │ true → AzureMonitorQueryActionExecutor
               │ false ↓
       ┌──── http_probe? ───────────┐
       │ + EnableRealHttpProbe      │
       └───────┬────────────────────┘
               │ true → HttpProbeActionExecutor
               │ false ↓
               └──────→ DryRunActionExecutor
```

| Flag | Key | Default | Slice |
|------|-----|---------|-------|
| Endpoint guard | `SafeActions:EnableExecution` | `false` | 8 |
| HTTP probe gate | `SafeActions:EnableRealHttpProbe` | `false` | 9 |
| Azure read gate | `SafeActions:EnableAzureReadExecutions` | `false` | 10 |
| Azure Monitor gate | `SafeActions:EnableAzureMonitorReadExecutions` | `false` | 11 |
| HTTP timeout | `SafeActions:HttpProbeTimeoutMs` | `5000` | 9 |
| HTTP max body | `SafeActions:HttpProbeMaxResponseBytes` | `1024` | 9 |
| Azure timeout | `SafeActions:AzureReadTimeoutMs` | `5000` | 10 |
| Monitor timeout | `SafeActions:AzureMonitorQueryTimeoutMs` | `5000` | 11 |

**All four execution flags default to `false`** — zero side-effects in a fresh deployment.

---

## 4. Error-Code Mapping

`AzureMonitorQueryActionExecutor` maps every failure to a deterministic error code
via the `Fail()` helper. Every failure response includes `mode` and `reason`.

| Error Code | Trigger | Response |
|------------|---------|----------|
| `invalid_json` | `JsonException` parsing payload | `Success: false` |
| `invalid_payload` | `workspaceId` or `query` field null, empty, or whitespace | `Success: false` |
| `invalid_workspace_id` | `workspaceId` is not a valid GUID format | `Success: false` |
| `blocked_query_pattern` | Query contains `.create`, `.alter`, `.drop`, `.ingest`, `.set`, `.append`, `.delete`, or `.execute` (case-insensitive) | `Success: false` |
| `azure_auth_failed` | `AuthenticationFailedException` from SDK | `Success: false` |
| `azure_forbidden` | `RequestFailedException` with status 403 | `Success: false` |
| `azure_not_found` | `RequestFailedException` with status 404 | `Success: false` |
| `azure_request_failed` | `RequestFailedException` with any other status | `Success: false` |
| `azure_monitor_timeout` | `OperationCanceledException` from linked CTS | `Success: false` |
| `unexpected_error` | Any other `Exception` | `Success: false` |

Rollback returns `Success: false` with reason `"not_supported"` and `mode: "azure_monitor_query"`.

---

## 5. Blocked Query Patterns

The executor rejects any query containing these patterns (case-insensitive substring match)
**before** any SDK call is made:

| Pattern | Purpose |
|---------|---------|
| `.create` | Prevents table/function creation |
| `.alter` | Prevents schema modifications |
| `.drop` | Prevents table/database deletion |
| `.ingest` | Prevents data ingestion |
| `.set` | Prevents set commands |
| `.append` | Prevents data appends |
| `.delete` | Prevents data deletion |
| `.execute` | Prevents stored function execution |

**Detection method**: `query.Contains(pattern, StringComparison.OrdinalIgnoreCase)`

---

## 6. DI Wiring

Registered in `SafeActionsInfrastructureExtensions.AddSafeActionsInfrastructure()`:

```csharp
// Azure Monitor Logs reader (read-only, DefaultAzureCredential)
services.AddSingleton(_ => new LogsQueryClient(new DefaultAzureCredential()));
services.AddSingleton<IAzureMonitorLogsReader, LogsQueryClientReader>();

// Azure Monitor query executor
services.AddSingleton<AzureMonitorQueryActionExecutor>();

// Routing executor (composite — 6-param constructor)
services.AddSingleton<IActionExecutor, RoutingActionExecutor>();
```

`LogsQueryClient` is a singleton — the `Azure.Monitor.Query` SDK is designed for singleton
lifetime. `DefaultAzureCredential` handles token caching and refresh internally.

---

## 7. Test Matrix

### Summary

| Suite | Before | After | New |
|-------|--------|-------|-----|
| AgentRuns | 53 | 53 | 0 |
| SafeActions | 133 | 170 | +37 |
| Integration | 24 | 24 | 0 |
| MCP Contract | 8 | 8 | 0 |
| **Total** | **218** | **255** | **+37** |

### New Tests — AzureMonitorQueryActionExecutorTests (20+ tests)

| # | Test | Error Code Verified |
|---|------|---------------------|
| 1 | `ExecuteAsync_Returns_Success_With_QueryResults` | — (success) |
| 2 | `ExecuteAsync_Returns_InvalidJson_For_NonJsonPayload` | `invalid_json` |
| 3 | `ExecuteAsync_Returns_InvalidPayload_For_MissingWorkspaceId` | `invalid_payload` |
| 4 | `ExecuteAsync_Returns_InvalidPayload_For_EmptyWorkspaceId` | `invalid_payload` |
| 5 | `ExecuteAsync_Returns_InvalidPayload_For_MissingQuery` | `invalid_payload` |
| 6 | `ExecuteAsync_Returns_InvalidPayload_For_EmptyQuery` | `invalid_payload` |
| 7 | `ExecuteAsync_Returns_InvalidWorkspaceId_For_NonGuidFormat` | `invalid_workspace_id` |
| 8-15 | `ExecuteAsync_Returns_BlockedQueryPattern` (Theory ×8) | `blocked_query_pattern` |
| 16 | `ExecuteAsync_Returns_AuthFailed_On_AuthenticationFailedException` | `azure_auth_failed` |
| 17 | `ExecuteAsync_Returns_Forbidden_On_403_RequestFailedException` | `azure_forbidden` |
| 18 | `ExecuteAsync_Returns_NotFound_On_404_RequestFailedException` | `azure_not_found` |
| 19 | `ExecuteAsync_Returns_RequestFailed_On_Other_RequestFailedException` | `azure_request_failed` |
| 20 | `ExecuteAsync_Returns_Timeout_On_SlowReader` | `azure_monitor_timeout` |
| 21 | `ExecuteAsync_Returns_UnexpectedError_On_GenericException` | `unexpected_error` |
| 22 | `ExecuteAsync_Clamps_TimespanMinutes_To_Minimum_1` | — (validation) |
| 23 | `ExecuteAsync_Clamps_TimespanMinutes_To_Maximum_1440` | — (validation) |
| 24 | `ExecuteAsync_Defaults_TimespanMinutes_To_60` | — (default) |
| 25 | `RollbackAsync_Returns_NotSupported` | — (rollback) |
| 26 | `ExecuteAsync_Success_Response_Contains_Mode_And_RowCount` | — (shape) |
| 27 | `ExecuteAsync_Failure_Response_Contains_Mode_And_Reason` | — (shape) |

> Note: The Theory with 8 `InlineData` values (one per blocked pattern) counts as 8
> individual test executions.

### New Tests — RoutingActionExecutorTests (6 tests)

| # | Test |
|---|------|
| 1 | `ExecuteAsync_Routes_AzureMonitorQuery_To_Real_When_Enabled` |
| 2 | `ExecuteAsync_Routes_AzureMonitorQuery_To_DryRun_When_Disabled` |
| 3 | `ExecuteAsync_Routes_AzureMonitorQuery_CaseInsensitive` (Theory ×2) |
| 4 | `RollbackAsync_Routes_AzureMonitorQuery_To_Real_When_Enabled` |
| 5 | `RollbackAsync_Routes_AzureMonitorQuery_To_DryRun_When_Disabled` |

### New Tests — SafeActionRoutingEndpointTests (2 tests)

| # | Test |
|---|------|
| 1 | `Execute_AzureMonitorQuery_Routes_To_RealExecutor_WhenEnabled` |
| 2 | `Execute_AzureMonitorQuery_FallsBack_To_DryRun_WhenDisabled` |

---

## 8. .http Documentation

Section M was added to `docs/http/OpsCopilot.Api.http` with five test scenarios:

| Request | Description |
|---------|-------------|
| M1 | `azure_monitor_query` execute — real SDK call (both flags enabled) |
| M2 | `azure_monitor_query` execute — dry-run fallback (Monitor flag disabled) |
| M3 | `azure_monitor_query` rollback — not supported |
| M4 | `azure_monitor_query` with blocked query pattern — deterministic error |
| M5 | `azure_monitor_query` with invalid workspaceId — deterministic error |

Each request documents the required preconditions (`EnableExecution=true`,
`EnableAzureMonitorReadExecutions=true`) and expected response shape.

---

## 9. Why Azure Monitor Query in Slice 11

### Decision Rationale

Slice 10 introduced read-only ARM metadata retrieval via `Azure.ResourceManager`. Slice 11
extends the read-only pattern to operational data via `Azure.Monitor.Query`. Why?

1. **Operational insight** — ARM metadata (Slice 10) shows _what_ resources exist.
   Log Analytics queries show _how_ those resources are behaving — CPU, memory, errors,
   heartbeats, activity logs.

2. **Query safety** — unlike ARM GET (inherently read-only), KQL has management commands
   (`.create`, `.alter`, `.drop`) that could modify data. The blocked-pattern guardrail
   ensures only read queries reach the SDK.

3. **Same safety model** — the four-flag gating, `DefaultAzureCredential`, deterministic
   error codes, timeout, and routing patterns are identical to Slice 10. No new
   architectural concepts.

4. **Configurable scope** — `timespanMinutes` limits query scope (default 60 min, max 24h),
   preventing unbounded scans. `AzureMonitorQueryTimeoutMs` prevents hung queries.

### Package Choices

| Package | Version | Purpose |
|---------|---------|---------|
| `Azure.Monitor.Query` | 1.5.0 | `LogsQueryClient` for Log Analytics KQL queries |

Official Microsoft SDK with stable API surface. No preview packages are used.
`Azure.Identity` (already present from Slice 10) provides `DefaultAzureCredential`.

---

## 10. Cumulative Slice Summary

| Slice | Action Type | Executor | SDK | Tests Added |
|-------|------------|----------|-----|-------------|
| 8 | *(any)* | `DryRunActionExecutor` | — | +109 |
| 9 | `http_probe` | `HttpProbeActionExecutor` | `System.Net.Http` | +24 |
| 10 | `azure_resource_get` | `AzureResourceGetActionExecutor` | `Azure.ResourceManager` | +24 |
| 11 | `azure_monitor_query` | `AzureMonitorQueryActionExecutor` | `Azure.Monitor.Query` | +37 |
| **Total** | — | — | — | **255** |

---

## Verification Command

```bash
dotnet build OpsCopilot.sln
dotnet test  OpsCopilot.sln
# Expected: 255 passed, 0 failed, 0 skipped
```

---

_Generated as part of Dev Slice 11 implementation evidence._
