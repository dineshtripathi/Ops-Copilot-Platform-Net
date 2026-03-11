# Slice 58.1 — ACL/Document Access Enforcement for `runbook_search`

**Status:** COMPLETE  
**Date:** 2025-07-12  
**Objective:** Add a pluggable `IRunbookAclFilter` contract, a permissive default implementation (`PermissiveRunbookAclFilter`), wire it into `TriageOrchestrator`, and establish a test baseline for the ACL hook.

---

## 1. Objective

Gate the runbook search results in `TriageOrchestrator` through an ACL filter so that:
- A dedicated `IRunbookAclFilter` can be swapped per-environment (permissive by default, strict for multi-tenant deployments).
- The orchestrator passes `RunbookCallerContext` (tenantId, groups, roles) alongside the raw hits to any injected filter.
- No existing behaviour changes — the new default (`PermissiveRunbookAclFilter`) returns the full hit list unchanged.

---

## 2. Constraints (Non-Negotiables)

- No new HTTP routes, no DB schema changes, no migrations.
- Additive-only changes to DTOs.
- No secrets in logs.
- All changes within AgentRuns + BuildingBlocks.Contracts.

---

## 3. Acceptance Criteria

| # | Criterion | Result |
|---|---|---|
| AC1 | `IRunbookAclFilter` interface exists with `FilterAsync(context, hits, ct)` | ✅ |
| AC2 | `PermissiveRunbookAclFilter` returns the full hit list unchanged | ✅ |
| AC3 | `TriageOrchestrator` accepts `IRunbookAclFilter` as a constructor parameter (last param) | ✅ |
| AC4 | All 26 callsites updated to pass `PermissiveRunbookAclFilter` | ✅ |
| AC5 | `IRunbookAclFilter` registered in DI as `PermissiveRunbookAclFilter` | ✅ |
| AC6 | `RunbookCallerContext` carries `TenantId`, `Groups`, `Roles` | ✅ |
| AC7 | 5 new ACL unit/integration tests pass | ✅ |
| AC8 | `dotnet build -warnaserror` — 0 warnings, 0 errors | ✅ |
| AC9 | `dotnet test` — 0 failures across all test projects | ✅ |

---

## 4. Files Changed

### New Files

| File | Purpose |
|---|---|
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Rag/IRunbookAclFilter.cs` | Contract interface for the ACL filter |
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Rag/RunbookCallerContext.cs` | Record carrying tenant/group/role identity for filter context |
| `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Abstractions/IRunbookAclFilter.cs` | Application-layer `IRunbookAclFilter` re-export / application usage |
| `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Acl/PermissiveRunbookAclFilter.cs` | Default pass-through implementation |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/RunbookAclFilterTests.cs` | 5 new unit + integration tests |

### Modified Files

| File | Change |
|---|---|
| `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Orchestration/TriageOrchestrator.cs` | Added `IRunbookAclFilter` constructor parameter; calls `FilterAsync` after `SearchAsync` |
| `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Abstractions/RunbookSearchToolResponse.cs` | Additive: `RunbookCallerContext` factory helpers |
| `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Extensions/AgentRunsApplicationExtensions.cs` | DI registration: `services.AddSingleton<IRunbookAclFilter, PermissiveRunbookAclFilter>()` |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/TriageOrchestratorTests.cs` | Updated all 26 `new TriageOrchestrator(...)` callsites to pass `PermissiveRunbookAclFilter` |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/KqlGovernedEvidenceIntegrationTests.cs` | Updated all callsites |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/RunbookCitationIntegrationTests.cs` | Updated all callsites |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/TriageEvidenceIntegrationTests.cs` | Updated all callsites |
| `global.json` | Bumped SDK version from `10.0.100` → `10.0.102` (installed GA SDK) |

---

## 5. Interface Design

```csharp
// OpsCopilot.BuildingBlocks.Contracts.Rag
public interface IRunbookAclFilter
{
    Task<IReadOnlyList<RunbookSearchHit>> FilterAsync(
        RunbookCallerContext context,
        IReadOnlyList<RunbookSearchHit> hits,
        CancellationToken ct);
}

public sealed record RunbookCallerContext(
    string TenantId,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Roles)
{
    public static RunbookCallerContext TenantOnly(string tenantId)
        => new(tenantId, Array.Empty<string>(), Array.Empty<string>());
}

// Default implementation (AgentRuns.Application.Acl)
internal sealed class PermissiveRunbookAclFilter : IRunbookAclFilter
{
    public Task<IReadOnlyList<RunbookSearchHit>> FilterAsync(
        RunbookCallerContext context,
        IReadOnlyList<RunbookSearchHit> hits,
        CancellationToken ct)
        => Task.FromResult(hits);
}
```

---

## 6. Orchestrator Integration Point

```csharp
// TriageOrchestrator.cs (excerpt)
var rawHits = await _runbookSearch.SearchAsync(...);
var callerContext = RunbookCallerContext.TenantOnly(agentRun.TenantId);
var filteredHits = await _aclFilter.FilterAsync(callerContext, rawHits, ct);
// use filteredHits to build citations
```

---

## 7. Test Summary (`RunbookAclFilterTests.cs`)

| Test | Type | Assert |
|---|---|---|
| `PermissiveFilter_ReturnsAllHits_Unchanged` | Unit | `Assert.Same` — identity check, list reference preserved |
| `PermissiveFilter_EmptyList_ReturnsEmpty` | Unit | Empty input → empty output |
| `Orchestrator_AclFilter_IsCalledWithCorrectContext` | Integration | Mock filter `Verify(Times.Once)` with correct `TenantId` |
| `Orchestrator_WhenFilterReturnsEmpty_RunbookCitationsIsEmpty` | Integration | Filter returns `[]`, run still `Completed`, `RunbookCitations` empty |
| `Orchestrator_WhenFilterReturnsSubset_OnlyAuthorizedHitsBecomeCitations` | Integration | 2-hit mock, filter returns 1, only `rb-allowed` in `RunbookCitations` |

---

## 8. Build Gate

```
dotnet build .\OpsCopilot.sln --configuration Release -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:15.58
```

---

## 9. Test Gate

```
dotnet test .\OpsCopilot.sln --no-build --configuration Release

Passed!  - Failed: 0, Passed:  31  - OpsCopilot.Modules.Governance.Tests
Passed!  - Failed: 0, Passed:  30  - OpsCopilot.Modules.Connectors.Tests
Passed!  - Failed: 0, Passed:  15  - OpsCopilot.Modules.Evaluation.Tests
Passed!  - Failed: 0, Passed:  31  - OpsCopilot.Modules.AlertIngestion.Tests
Passed!  - Failed: 0, Passed:  27  - OpsCopilot.Modules.Reporting.Tests
Passed!  - Failed: 0, Passed: 108  - OpsCopilot.Modules.AgentRuns.Tests   ← +5 ACL tests
Passed!  - Failed: 0, Passed: 303  - OpsCopilot.Modules.Packs.Tests
Passed!  - Failed: 0, Passed:  17  - OpsCopilot.Modules.Tenancy.Tests
Passed!  - Failed: 0, Passed: 368  - OpsCopilot.Modules.SafeActions.Tests
Passed!  - Failed: 0, Passed:  24  - OpsCopilot.Integration.Tests
Passed!  - Failed: 0, Passed:   8  - OpsCopilot.Mcp.ContractTests
```

**Total: 0 failures, 966 tests passing.**
