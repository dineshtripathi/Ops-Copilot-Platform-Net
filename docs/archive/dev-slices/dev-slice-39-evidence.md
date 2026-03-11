# Slice 39 — Pack Evidence Execution Fix (Mode B Real, STRICT)

## Goal

Fix Slice 38's non-functional Mode B pack evidence execution. The stub
`ExecuteQueryAsync` on `IObservabilityConnector` silently returned
`Success: false` with no error detail — Mode B evidence was completely broken.

**Critical design mandate**: Do NOT extend `IObservabilityConnector`. Instead,
introduce a dedicated interface: `IObservabilityQueryExecutor`.

**STRICT constraints**: No new routes, no DB schema/migrations, no DTO breaking
changes, no SafeActions changes, no write operations, no secrets in logs,
Mode A unchanged.

---

## Architecture

### Problem

Slice 38 added `ExecuteQueryAsync` to `IObservabilityConnector` but the
`AzureMonitorObservabilityConnector` implementation was a stub that always
returned `new QueryExecutionResult(false, null, 0, "Not implemented")`.
This meant Mode B evidence execution never actually reached Azure Monitor.

### Solution — Dedicated IObservabilityQueryExecutor

```
IObservabilityConnector (metadata/capabilities ONLY — no query execution)
IObservabilityQueryExecutor (NEW — dedicated query execution interface)
  └── AzureMonitorObservabilityQueryExecutor (NEW — real implementation)
        ├── Azure.Monitor.Query (LogsQueryClient)
        ├── Azure.Identity (DefaultAzureCredential)
        └── Safety guardrails (blocked patterns, row cap, payload cap, timeout)
```

### Data Flow (Updated)

```
AgentRunEndpoints (triage POST)
  → IPackEvidenceExecutor.ExecuteAsync(packId, alertContext)
    → Double-gate check:
        1. Pack deployment mode >= B
        2. Packs:EvidenceExecutionEnabled = true
    → IPackCatalog.GetPackByIdAsync(packId)
    → For each evidence collector:
        → IPackFileReader.ReadFileAsync(queryFile) → KQL content
        → IObservabilityQueryExecutor.ExecuteQueryAsync(workspaceId, kql, timespan)
        → Truncate (EvidenceMaxRows=50, EvidenceMaxChars=4000)
        → Map result → PackEvidenceItem
    → Return PackEvidenceExecutionResult
  → Map to PackEvidenceResultDto list
  → Attach to TriageResponse.PackEvidenceResults
```

### AzureMonitorObservabilityQueryExecutor Safety Guardrails

| Guardrail | Detail |
|-----------|--------|
| **Blocked patterns** | `.create`, `.alter`, `.drop`, `.ingest`, `.set`, `.append`, `.delete`, `.execute` |
| **Row cap** | 200 rows maximum |
| **Payload cap** | 20 KB maximum |
| **Timespan clamp** | 1–1440 minutes, default 60 |
| **Query timeout** | Configurable via `Packs:AzureMonitorQueryTimeoutMs`, default 5000 ms |
| **Error codes** | `invalid_query`, `blocked_query_pattern`, `azure_auth_failed`, `azure_forbidden`, `azure_not_found`, `azure_request_failed`, `azure_monitor_timeout`, `unexpected_error` |

### Key Design Decisions

- **Separate interface**: `IObservabilityQueryExecutor` keeps query execution
  cleanly separated from connector metadata/capabilities (`IObservabilityConnector`).
- **Enhanced QueryExecutionResult**: Added `Columns`, `DurationMs`, `ErrorCode`
  fields with backward-compatible defaults.
- **Double-gate preserved**: Execution requires BOTH `deploymentMode >= B` AND
  `Packs:EvidenceExecutionEnabled = true` — same gates as Slice 38 but now
  the execution actually works.
- **Null vs empty**: When evidence is disabled or mode is A, `PackEvidenceResults`
  is `null` (not `[]`), preserving backward compatibility.
- **Error isolation**: Individual evidence collector failures are captured in
  `PackEvidenceItem.ErrorMessage` and `PackErrors[]` but never fail the triage.

---

## Files Created

| # | File | Purpose |
|---|------|---------|
| 1 | `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/IObservabilityQueryExecutor.cs` | Dedicated interface: `ExecuteQueryAsync(workspaceId, queryText, timespan, ct)` → `Task<QueryExecutionResult>` |
| 2 | `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/Connectors/AzureMonitorObservabilityQueryExecutor.cs` | Real Azure Monitor implementation (~189 lines) using `LogsQueryClient` + `DefaultAzureCredential` with full safety guardrails |

## Files Modified

| # | File | Change |
|---|------|--------|
| 3 | `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/IObservabilityConnector.cs` | Removed `ExecuteQueryAsync` — interface now metadata/capabilities only |
| 4 | `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/QueryExecutionResult.cs` | Enhanced with `Columns`, `DurationMs`, `ErrorCode` fields (backward-compatible defaults) |
| 5 | `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/Connectors/AzureMonitorObservabilityConnector.cs` | Removed stub `ExecuteQueryAsync` implementation |
| 6 | `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/OpsCopilot.Connectors.Infrastructure.csproj` | Added `Azure.Identity 1.13.2`, `Azure.Monitor.Query 1.5.0`, `Microsoft.Extensions.Configuration.Abstractions 9.0.2` |
| 7 | `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/Extensions/ConnectorInfrastructureExtensions.cs` | Registered `IObservabilityQueryExecutor` → `AzureMonitorObservabilityQueryExecutor` in DI |
| 8 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackEvidenceExecutor.cs` | Rewired from `IConnectorRegistry` to `IObservabilityQueryExecutor` — direct query execution |
| 9 | `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackEvidenceExecutorTests.cs` | All 14 unit tests rewritten for new `IObservabilityQueryExecutor` dependency |
| 10 | `docs/http/OpsCopilot.Api.http` | Added Section AN — Pack Evidence Execution Fix (6 requests, AN1–AN6) |

---

## Test Results

### Unit Tests — PackEvidenceExecutorTests (14 tests)

| Test | Status |
|------|--------|
| `ExecuteAsync_ModeA_SkipsExecution` | ✅ |
| `ExecuteAsync_FeatureDisabled_SkipsExecution` | ✅ |
| `ExecuteAsync_ModeBEnabled_ExecutesEvidence` | ✅ |
| `ExecuteAsync_CatalogThrows_ReturnsError` | ✅ |
| `ExecuteAsync_NoEligiblePacks_ReturnsEmpty` | ✅ |
| `ExecuteAsync_InvalidPack_IsSkipped` | ✅ |
| `ExecuteAsync_NoEligibleCollectors_ReturnsEmpty` | ✅ |
| `ExecuteAsync_QueryFileNotFound_AddsErrorAndItem` | ✅ |
| `ExecuteAsync_QueryExecutorReturnsError_AddsErrorAndItem` | ✅ |
| `ExecuteAsync_QueryFails_AddsErrorToItemAndErrorList` | ✅ |
| `ExecuteAsync_LargeResult_IsTruncated` | ✅ |
| `ExecuteAsync_MultiplePacks_AggregatesResults` | ✅ |
| `ExecuteAsync_CollectorThrows_AddsErrorButContinues` | ✅ |
| `ExecuteAsync_ModeC_IncludesModeAAndBCollectors` | ✅ |

### Integration Tests — TriageEvidenceIntegrationTests (10 tests, unchanged from Slice 38)

| Test | Status |
|------|--------|
| `ModeB_Enabled_ReturnsPackEvidenceResults` | ✅ |
| `ModeA_ReturnsNullPackEvidenceResults` | ✅ |
| `FeatureDisabled_ReturnsNullPackEvidenceResults` | ✅ |
| `EvidenceErrors_DoNotFailTriage_Returns200` | ✅ |
| `MultipleEvidenceItems_AggregateInResponse` | ✅ |
| `EvidenceFields_MapCorrectlyThroughPipeline` | ✅ |
| `ModeC_IncludesEvidenceResults` | ✅ |
| `EmptyEvidenceList_ReturnsNullPackEvidenceResults` | ✅ |
| `MixedSuccessAndFailure_BothPresentInResults` | ✅ |
| `RunIdAndStatus_FlowCorrectlyAlongsideEvidence` | ✅ |

---

## Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| AC-1 | `ExecuteQueryAsync` removed from `IObservabilityConnector` | ✅ |
| AC-2 | New `IObservabilityQueryExecutor` interface in Connectors.Abstractions | ✅ |
| AC-3 | `AzureMonitorObservabilityQueryExecutor` implements real Azure Monitor queries | ✅ |
| AC-4 | Safety guardrails: blocked patterns, row cap, payload cap, timeout | ✅ |
| AC-5 | `PackEvidenceExecutor` rewired to `IObservabilityQueryExecutor` | ✅ |
| AC-6 | Double-gate preserved (mode >= B + EvidenceExecutionEnabled) | ✅ Unit + integration tested |
| AC-7 | Mode A triage unchanged — `PackEvidenceResults` is `null` | ✅ Unit + integration tested |
| AC-8 | Mode B/C triage executes evidence when enabled | ✅ Unit + integration tested |
| AC-9 | Evidence errors do not fail triage (200 response preserved) | ✅ Unit + integration tested |
| AC-10 | `QueryExecutionResult` enhanced with Columns, DurationMs, ErrorCode | ✅ Backward-compatible defaults |
| AC-11 | DI wiring registers `IObservabilityQueryExecutor` | ✅ |
| AC-12 | NuGet packages added: Azure.Identity, Azure.Monitor.Query, Config.Abstractions | ✅ |
| AC-13 | No new routes added | ✅ Verified |
| AC-14 | No DB schema or migration changes | ✅ Verified |
| AC-15 | No SafeActions changes | ✅ Verified |
| AC-16 | No write operations in evidence path | ✅ Read-only queries only |
| AC-17 | No secrets logged | ✅ Verified |
| AC-18 | 14 unit tests passing | ✅ 14 passing |
| AC-19 | 10 integration tests passing | ✅ 10 passing |
| AC-20 | `dotnet build -warnaserror` → 0 warnings, 0 errors | ✅ Confirmed |
| AC-21 | `dotnet test` → all green | ✅ 745 passed, 0 failed |
| AC-22 | .http Section AN added (6 requests) | ✅ AN1–AN6 |
| AC-23 | Evidence doc committed | ✅ This document |

---

## Configuration

### Double-Gate Settings

```json
{
  "Packs": {
    "DeploymentMode": "ModeB",
    "EvidenceExecutionEnabled": true,
    "EvidenceMaxRows": 50,
    "EvidenceMaxChars": 4000,
    "AzureMonitorQueryTimeoutMs": 5000
  }
}
```

Both `DeploymentMode >= ModeB` AND `EvidenceExecutionEnabled = true` are
required for evidence execution. Either gate failing results in `null`
evidence results (not empty).

---

## Build & Test Gate

```
dotnet build OpsCopilot.sln -warnaserror
  Build succeeded.
  0 Warning(s)
  0 Error(s)

dotnet test OpsCopilot.sln --no-build --verbosity minimal
  Passed! — 11 assemblies, 745 tests, 0 failures

  Connectors.Tests ..........  30
  Governance.Tests ..........  31
  Evaluation.Tests ..........  15
  AgentRuns.Tests ...........  79
  AlertIngestion.Tests ......  31
  Reporting.Tests ...........  27
  Packs.Tests .............. 115
  Tenancy.Tests .............  17
  SafeActions.Tests ........ 368
  Integration.Tests .........  24
  Mcp.ContractTests ..........  8
                              ---
  Total                      745
```

---

## Summary

Slice 39 replaces Slice 38's non-functional evidence execution stub with a real
Azure Monitor implementation behind a dedicated `IObservabilityQueryExecutor`
interface. The `AzureMonitorObservabilityQueryExecutor` uses `LogsQueryClient`
with `DefaultAzureCredential` and enforces comprehensive safety guardrails
(blocked patterns, row/payload caps, configurable timeout, structured error
codes). `PackEvidenceExecutor` is rewired from `IConnectorRegistry` to
`IObservabilityQueryExecutor`, preserving the double-gate (mode + feature flag)
safety model. 10 files were created or modified, all 14 unit tests rewritten,
all 10 integration tests passing — 745 total solution tests, 0 failures,
0 warnings. All STRICT constraints are satisfied.
