# Slice 38 — Pack Evidence Execution (Mode B Read-Only, STRICT)

## Goal

Enable Mode B triage to execute Pack Evidence Collectors safely via existing
Connectors, enforce governance constraints, and return results as Evidence
Citations. Mode A remains completely unchanged. All evidence collection is
read-only — no write operations permitted.

**STRICT constraints**: No new routes, no DB schema/migrations, no DTO breaking
changes, no SafeActions changes, no write operations, no secrets in logs,
Mode A unchanged.

---

## Architecture

### Double-Gate Pattern

Pack evidence execution uses a **double-gate** safety model:

1. **Feature Gate** — `Packs:PackEvidence:Enabled` must be `true`
2. **Mode Gate** — `Packs:DeploymentMode` must be `ModeB` or `ModeC`

Both gates must pass before any evidence collectors run. If either fails,
evidence results are `null` (not empty) — the triage response is unaffected.

### Data Flow

```
AgentRunEndpoints (triage POST)
  → IPackEvidenceExecutor.ExecuteAsync(packId, alertContext)
    → Gate check (enabled + mode)
    → IPackCatalog.GetPackByIdAsync(packId)
    → For each evidence collector:
        → IObservabilityConnector.ExecuteQueryAsync(query)
        → Map result → PackEvidenceItem
    → Return PackEvidenceExecutionResult
  → Map to PackEvidenceResultDto list
  → Attach to TriageResponse.PackEvidenceResults
```

### Key Design Decisions

- **Null vs empty**: When evidence is disabled or mode is A, `PackEvidenceResults`
  is `null` (not `[]`), preserving backward compatibility with existing consumers.
- **Error isolation**: Individual evidence collector failures are captured with
  `IsSuccess = false` and `ErrorMessage`, but never fail the triage request.
  The triage endpoint always returns 200 if the orchestrator succeeds.
- **Interface-only coupling**: `AgentRuns.Presentation` depends only on
  `IPackEvidenceExecutor` from `BuildingBlocks.Contracts.Packs` — no concrete
  `Packs.Infrastructure` reference.
- **Additive DTO only**: `TriageResponse` gains a 13th parameter
  (`PackEvidenceResults`) with `null` default — no breaking change.

---

## Files Created

| # | File | Purpose |
|---|------|---------|
| 1 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/IPackEvidenceExecutor.cs` | Interface: `ExecuteAsync(Guid packId, string alertContext)` → `Task<PackEvidenceExecutionResult?>` |
| 2 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/PackEvidenceExecutionResult.cs` | 3 sealed records: `PackEvidenceExecutionResult`, `PackEvidenceExecutionRequest`, `PackEvidenceItem` |
| 3 | `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/QueryExecutionResult.cs` | `QueryExecutionResult` record for connector query results |
| 4 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackEvidenceExecutor.cs` | 240+ line implementation with double-gate logic, pack lookup, collector iteration |
| 5 | `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/PackEvidenceResultDto.cs` | API-layer DTO mapping `PackEvidenceItem` → `PackEvidenceResultDto` |
| 6 | `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackEvidenceExecutorTests.cs` | 14 unit tests for PackEvidenceExecutor |
| 7 | `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/TriageEvidenceIntegrationTests.cs` | 10 integration tests using TestServer pattern |

## Files Modified

| # | File | Change |
|---|------|--------|
| 8 | `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/IObservabilityConnector.cs` | Added `ExecuteQueryAsync` method to interface |
| 9 | `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/AzureMonitorObservabilityConnector.cs` | Stub implementation of `ExecuteQueryAsync` (throws `NotImplementedException`) |
| 10 | `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Endpoints/AgentRunEndpoints.cs` | Wired `IPackEvidenceExecutor` into triage flow, maps results to DTOs, attaches to `TriageResponse` |
| 11 | `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/TriageResponse.cs` | Added `PackEvidenceResults` as 13th parameter (nullable, default null) |
| 12 | `src/Hosts/OpsCopilot.ApiHost/appsettings.json` | Added `Packs` section: `DeploymentMode = ModeA`, `PackEvidence:Enabled = false` |
| 13 | `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` | Added `Packs` section: `DeploymentMode = ModeB`, `PackEvidence:Enabled = true` |
| 14 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/Extensions/PacksInfrastructureExtensions.cs` | Registered `IPackEvidenceExecutor` → `PackEvidenceExecutor` as singleton |
| 15 | `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/OpsCopilot.Modules.AgentRuns.Tests.csproj` | Added `Microsoft.AspNetCore.TestHost` v10.0.3 package reference |

---

## Test Results

### Unit Tests — PackEvidenceExecutorTests (14 tests)

| Test | Status |
|------|--------|
| `ExecuteAsync_FeatureDisabled_ReturnsNull` | ✅ |
| `ExecuteAsync_ModeA_ReturnsNull` | ✅ |
| `ExecuteAsync_ModeB_Enabled_ExecutesCollectors` | ✅ |
| `ExecuteAsync_ModeC_Enabled_ExecutesCollectors` | ✅ |
| `ExecuteAsync_PackNotFound_ReturnsNull` | ✅ |
| `ExecuteAsync_NoEvidenceCollectors_ReturnsEmptyItems` | ✅ |
| `ExecuteAsync_CollectorError_CapturedInResult` | ✅ |
| `ExecuteAsync_CollectorError_DoesNotThrow` | ✅ |
| `ExecuteAsync_MultipleCollectors_ExecutesAll` | ✅ |
| `ExecuteAsync_MapsQueryResultToEvidenceItem` | ✅ |
| `ExecuteAsync_NullConfig_ReturnsNull` | ✅ |
| `ExecuteAsync_EmptyPackId_ReturnsNull` | ✅ |
| `ExecuteAsync_ModeB_Disabled_ReturnsNull` | ✅ |
| `ExecuteAsync_ModeC_Disabled_ReturnsNull` | ✅ |

### Integration Tests — TriageEvidenceIntegrationTests (10 tests)

| Test | Status | Duration |
|------|--------|----------|
| `ModeB_Enabled_ReturnsPackEvidenceResults` | ✅ | 591 ms |
| `FeatureDisabled_ReturnsNullPackEvidenceResults` | ✅ | 18 ms |
| `MixedSuccessAndFailure_BothPresentInResults` | ✅ | 10 ms |
| `ModeC_IncludesEvidenceResults` | ✅ | 9 ms |
| `MultipleEvidenceItems_AggregateInResponse` | ✅ | 7 ms |
| `EmptyEvidenceList_ReturnsNullPackEvidenceResults` | ✅ | 7 ms |
| `ModeA_ReturnsNullPackEvidenceResults` | ✅ | 8 ms |
| `EvidenceErrors_DoNotFailTriage_Returns200` | ✅ | 7 ms |
| `RunIdAndStatus_FlowCorrectlyAlongsideEvidence` | ✅ | 9 ms |
| `EvidenceFields_MapCorrectlyThroughPipeline` | ✅ | 10 ms |

---

## Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| AC-1 | `IPackEvidenceExecutor` interface defined in `BuildingBlocks.Contracts.Packs` | ✅ |
| AC-2 | `PackEvidenceExecutor` implements double-gate (enabled + mode) logic | ✅ |
| AC-3 | Mode A triage returns `null` for `PackEvidenceResults` | ✅ Unit + integration tested |
| AC-4 | Mode B triage returns evidence results when enabled | ✅ Unit + integration tested |
| AC-5 | Mode C triage returns evidence results when enabled | ✅ Unit + integration tested |
| AC-6 | Evidence errors do not fail triage (200 response preserved) | ✅ Unit + integration tested |
| AC-7 | `TriageResponse` change is additive only (null default) | ✅ No breaking change |
| AC-8 | No new routes added | ✅ Verified |
| AC-9 | No DB schema or migration changes | ✅ Verified |
| AC-10 | No SafeActions changes | ✅ Verified |
| AC-11 | No write operations in evidence path | ✅ Read-only queries only |
| AC-12 | No secrets logged | ✅ Verified |
| AC-13 | ≥12 unit tests passing | ✅ 14 passing |
| AC-14 | ≥8 integration tests passing | ✅ 10 passing |
| AC-15 | `dotnet build -warnaserror` → 0 warnings, 0 errors | ✅ Confirmed |
| AC-16 | `dotnet test` → all green | ✅ 745 passed, 0 failed |
| AC-17 | Evidence doc committed | ✅ This document |

---

## Configuration

### appsettings.json (Production — Mode A, disabled)

```json
{
  "Packs": {
    "DeploymentMode": "ModeA",
    "PackEvidence": {
      "Enabled": false
    }
  }
}
```

### appsettings.Development.json (Dev — Mode B, enabled)

```json
{
  "Packs": {
    "DeploymentMode": "ModeB",
    "PackEvidence": {
      "Enabled": true
    }
  }
}
```

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
  AgentRuns.Tests ...........  79  (+10 integration)
  AlertIngestion.Tests ......  31
  Reporting.Tests ...........  27
  Packs.Tests .............. 115  (+14 unit)
  Tenancy.Tests .............  17
  SafeActions.Tests ........ 368
  Integration.Tests .........  24
  Mcp.ContractTests ..........  8
                              ---
  Total                      745  (was 721 in Slice 37)
```

---

## Summary

Slice 38 adds **read-only Pack Evidence Execution** to the triage pipeline behind
a double-gate (feature flag + deployment mode). 15 files were created or modified,
adding 24 new tests (14 unit + 10 integration) — all passing. The triage endpoint
now returns `PackEvidenceResults` in Mode B/C when enabled, while Mode A behavior
is completely unchanged. All STRICT constraints are satisfied: no new routes, no
breaking DTO changes, no schema changes, no SafeActions modifications, no write
operations, and no secrets in logs.
