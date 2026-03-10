# Slice 58 Evidence — Governed `runbook_search` RAG Integration with Citation Mapping

## Objective

Integrate a `runbook_search` MCP tool into the triage orchestration pipeline so that:

- Runbook search results are fetched via a governed `IRunbookSearchToolClient`.
- Allowlist, budget, and degraded-mode policies are enforced before calling the tool.
- Matching runbooks are mapped to `RunbookCitation` objects and surfaced in the `AgentRunResponse.RunbookCitations` collection.
- All existing policy paths (deny, budget exhaustion, tool exception, `Ok:false` result) are covered by integration tests.

---

## Baseline

| Metric | Value |
|---|---|
| Commit | `6a21fa7` |
| Tests | 951 |
| Failures | 0 |
| Build warnings | 0 |

---

## What Was Implemented

### New Contracts

| Type | Location |
|---|---|
| `IRunbookSearchToolClient` | `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Contracts/` |
| `RunbookSearchToolRequest` | same |
| `RunbookSearchToolResponse` / `RunbookSearchHit` | same |
| `RunbookCitation` (response DTO field) | `AgentRunResponse` in Contracts |

### New Infrastructure

| Type | Location |
|---|---|
| `RunbookSearchToolClient` | `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Infrastructure/` |

Implements `IRunbookSearchToolClient`, calls the MCP `runbook_search` tool, deserialises hits, and returns a `RunbookSearchToolResponse`.

### Orchestrator Integration (`TriageOrchestrator`)

1. **Allowlist gate** — `IToolAllowlistPolicy.IsAllowed("runbook_search")` checked before invoking client.
2. **Budget gate** — `ITokenBudgetPolicy.TryConsume(…)` checked before invoking client.
3. **Tool call** — `IRunbookSearchToolClient.SearchAsync(request)` called inside a `try/catch`.
4. **Degraded-mode** — `catch` block routes to `IDegradedModePolicy.MapFailure(…)`.
5. **Citation mapping** — `response.Hits` mapped to `RunbookCitation { RunbookId, Title, Summary, Score }` and stored in `AgentRunResponse.RunbookCitations`.

---

## Integration Tests

**File:** `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/RunbookCitationIntegrationTests.cs`

All tests use an inline `WebApplicationFactory` pattern (no `WebApplicationFactory<>` base class):
- `WebApplication.CreateBuilder` → `UseTestServer()` → `MapAgentRunEndpoints()` → `StartAsync()`
- All policy and repo mocks are `MockBehavior.Strict`
- `IPackSafeActionProposer` / `IPackSafeActionRecorder` use default `Mock<>` (not strict)

| # | Test | Key Assertion |
|---|---|---|
| 1 | `BothToolsSucceed_ResponseContainsRunbookCitations` | One hit `("high-cpu","High CPU Runbook","Check top processes.",0.85)` → `RunbookCitations` not empty, first `RunbookId == "high-cpu"` |
| 2 | `RunbookAllowlistDenied_EmptyRunbookCitations_RunCompletes` | Allowlist returns `Deny("TOOL_NOT_ALLOWED")` → `RunbookCitations` is empty; Strict mock confirms `ExecuteAsync` never called |
| 3 | `RunbookBudgetDenied_EmptyRunbookCitations_RunCompletes` | Budget `SetupSequence` Allow then `Deny("BUDGET_EXHAUSTED")` → `RunbookCitations` is empty; Strict mock confirms `ExecuteAsync` never called |
| 4 | `RunbookToolThrows_EmptyRunbookCitations_RunCompletes` | `ExecuteAsync` throws `Exception("mcp-error")` → `MapFailure` returns `DegradedDecision(true,"UNKNOWN_FAILURE",…,false)` → run completes, `RunbookCitations` empty |
| 5 | `RunbookReturnsNotOk_EmptyRunbookCitations_RunCompletes` | Client returns `RunbookSearchToolResponse(Ok:false,[],…)` → Strict degraded has **no** `MapFailure` setup (proves `Ok:false` is not an exception path) → `RunbookCitations` empty |
| 6 | `RunbookReturnsMultipleHits_AllMappedToResponse` | 3 hits (`"rb-cpu"`, `"rb-mem"`, `"rb-disk"`) → `Count == 3`, all IDs present |

---

## Build Gate

```
dotnet build OpsCopilot.sln -warnaserror
Build succeeded. 0 Warning(s). 0 Error(s).
```

---

## Test Gate

```
dotnet test OpsCopilot.sln
```

| Assembly | Passed | Failed |
|---|---|---|
| `OpsCopilot.Modules.Governance.Tests` | 31 | 0 |
| `OpsCopilot.Modules.Connectors.Tests` | 30 | 0 |
| `OpsCopilot.Modules.Evaluation.Tests` | 15 | 0 |
| `OpsCopilot.Modules.AlertIngestion.Tests` | 31 | 0 |
| `OpsCopilot.Modules.Reporting.Tests` | 27 | 0 |
| `OpsCopilot.Modules.AgentRuns.Tests` | 103 | 0 |
| `OpsCopilot.Modules.Tenancy.Tests` | 17 | 0 |
| `OpsCopilot.Modules.Packs.Tests` | 303 | 0 |
| `OpsCopilot.Modules.SafeActions.Tests` | 368 | 0 |
| `OpsCopilot.Integration.Tests` | 24 | 0 |
| `OpsCopilot.Mcp.ContractTests` | 8 | 0 |
| **Total** | **957** | **0** |

Delta: +6 tests (baseline 951 → 957). Zero regressions.

---

## Acceptance Criteria

| AC | Status |
|---|---|
| `IRunbookSearchToolClient` + `RunbookSearchToolClient` implemented | ✅ |
| `RunbookCitation` added to `AgentRunResponse` | ✅ |
| Orchestrator enforces allowlist before calling tool | ✅ |
| Orchestrator enforces budget before calling tool | ✅ |
| Tool exception routed to degraded-mode policy | ✅ |
| `Ok:false` result handled gracefully (empty citations, no exception) | ✅ |
| ≥ 6 integration tests asserting `RunbookCitations` in HTTP response | ✅ (6) |
| Build: 0 errors, 0 warnings | ✅ |
| Full suite: 0 regressions | ✅ |
