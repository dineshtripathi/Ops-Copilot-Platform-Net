# Slice 57.1 — Governed KQL Evidence Retrieval: Implementation Evidence

## Objective

Wire governed `kql_query` MCP invocation from `TriageOrchestrator` with:
- Full 3-guardrail governance (allowlist, budget, scope evaluator)
- Per-call tool ledgering (`ToolCall.Create(...)`) in both happy and error paths
- Citations derived exclusively from MCP tool responses (not from input parameters)
- Degraded-mode semantics when tool throws or returns `Ok=false`
- **Hard invariant**: `ApiHost`/`Infrastructure` MUST NOT reference `Azure.Monitor.Query` — KQL stays in `McpHost` only

---

## Pre-flight Check Results (8/8 PASS)

| # | Check | Result |
|---|---|---|
| 1 | Build gate (`dotnet build -warnaserror`) | ✅ 0 errors, 0 warnings |
| 2 | Baseline test suite (951 tests) | ✅ 951/951 passing, 0 failures |
| 3 | `IKqlToolClient` interface exists with correct signature | ✅ `Task<KqlToolResponse> ExecuteAsync(KqlToolRequest, CancellationToken)` |
| 4 | `McpStdioKqlToolClient` has zero `Azure.Monitor.Query` refs | ✅ Confirmed — MCP boundary enforced |
| 5 | All 3 guardrails wired in `TriageOrchestrator.RunAsync` | ✅ Allowlist → Budget → ScopeEvaluator |
| 6 | `ToolCall.Create(...)` called in **both** success and error paths | ✅ `AppendToolCallAsync` confirmed in both paths |
| 7 | Citations built from `KqlToolResponse` fields (not input) | ✅ `BuildCitation(r)` uses `r.WorkspaceId`, `r.ExecutedQuery`, etc. |
| 8 | `ITargetScopeEvaluator` (Guardrail 2.5) wired as optional | ✅ `_scopeEvaluator?.Evaluate(...)` — null-safe |

---

## Implementation Files

| File | Module | Role |
|---|---|---|
| `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Application/Orchestration/TriageOrchestrator.cs` | AgentRuns.Application | Orchestrator — all 3 guardrails, KQL call, ledgering, citations |
| `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Application/Ports/IKqlToolClient.cs` | AgentRuns.Application | Port interface — MCP tool boundary |
| `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Application/Ports/KqlToolRequest.cs` | AgentRuns.Application | Request DTO |
| `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Application/Ports/KqlToolResponse.cs` | AgentRuns.Application | Response DTO |
| `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Application/Domain/KqlCitation.cs` | AgentRuns.Application | Citation value object |
| `src/Hosts/McpHost/Infrastructure/McpStdioKqlToolClient.cs` | McpHost.Infrastructure | Adapter — launches MCP stdio child process, calls `kql_query` |
| `src/Hosts/McpHost/Tools/KqlQueryTool.cs` | McpHost | MCP tool — `Azure.Monitor.Query` boundary lives here only |

---

## Guardrail Code References

### Guardrail 1 — Tool Allowlist
```csharp
var allowDecision = await _allowlist.CanUseTool(tenantId, ToolName, ct);
if (!allowDecision.IsAllowed)
{
    await _repo.CompleteRunAsync(runId, AgentRunStatus.Failed, ...);
    return new TriageResult(TriageStatus.Failed, ...);
}
```

### Guardrail 2 — Token Budget
```csharp
var budgetDecision = await _budget.CheckRunBudget(tenantId, runId, ct);
if (!budgetDecision.IsAllowed)
{
    await _repo.CompleteRunAsync(runId, AgentRunStatus.Failed, ...);
    return new TriageResult(TriageStatus.Failed, ...);
}
```

### Guardrail 2.5 — Target Scope Evaluator (optional)
```csharp
if (_scopeEvaluator is not null)
{
    var scopeDecision = await _scopeEvaluator.Evaluate(tenantId, "LogAnalyticsWorkspace", workspaceId, ct);
    if (!scopeDecision.IsAllowed)
    {
        await _repo.CompleteRunAsync(runId, AgentRunStatus.Failed, ...);
        return new TriageResult(TriageStatus.Failed, ...);
    }
}
```

---

## MCP Boundary Proof

| Claim | Evidence |
|---|---|
| `McpStdioKqlToolClient.cs` has zero `Azure.Monitor.Query` references | Confirmed via grep — 0 matches |
| `Azure.Monitor.Query` is used **only** in `McpHost/Tools/KqlQueryTool.cs` | Confirmed — `LogsQueryClient` in McpHost only |
| `ApiHost` and `Infrastructure` projects have zero `Azure.Monitor.Query` refs | Confirmed |

**Transport details** (`McpStdioKqlToolClient.cs`):
- Lazy singleton child process via `SemaphoreSlim _initLock`
- `McpClient.CallToolAsync("kql_query", { workspaceId, kql, timespan }, ct)`
- Per-call timeout: `cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds))`
- Timeout handling: `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` → `ErrorResponse(..., "Timeout")` — never re-throws

---

## TriageOrchestrator RunAsync Flow

```
1. Resolve session → audit event
2. CreateRunAsync(tenantId, alertFingerprint) → AgentRun (Pending)
3. [GUARDRAIL 1] CanUseTool(tenantId, "kql_query")
   → Deny → CompleteRunAsync(Failed), return TriageResult(Failed)
4. [GUARDRAIL 2] CheckRunBudget(tenantId, runId)
   → Deny → CompleteRunAsync(Failed), return TriageResult(Failed)
5. [GUARDRAIL 2.5] scopeEvaluator?.Evaluate(tenantId, "LogAnalyticsWorkspace", workspaceId)
   → Deny → CompleteRunAsync(Failed), return TriageResult(Failed)
6. Stopwatch.Start(); ExecuteAsync(KqlToolRequest, ct)
   → Exception:
     a. _degraded.MapFailure(ex) → DegradedDecision
     b. BuildCitation(synthetic response) → KqlCitation
     c. AppendToolCallAsync(ToolCall.Create(runId, tool, req, resp, status, ms, citations))
     d. CompleteRunAsync(Degraded|Failed per IsDegraded flag)
     e. return TriageResult(Degraded|Failed)
   → Ok=false (non-throwing):
     a. BuildCitation(response)
     b. AppendToolCallAsync(...)
     c. CompleteRunAsync(Degraded)
     d. return TriageResult(Degraded)
   → Ok=true (happy path):
     a. BuildCitation(response)
     b. AppendToolCallAsync(...)
     c. RunbookSearch (guarded by same allowlist+budget)
     d. Optional LLM summarisation
     e. CompleteRunAsync(Completed, summaryJson, citationsJson)
     f. return TriageResult(Completed, ...)
```

---

## Test Coverage Summary

### All 10 Required Scenarios — CONFIRMED

| # | Scenario | Test File | Test Name | Line |
|---|---|---|---|---|
| 1 | Allowlist denies → Failed, KQL never called | `TriageOrchestratorTests.cs` | `RunAsync_ToolDeniedByAllowlist_ReturnsFailedAndNoMcpCall` | 366 |
| 2 | Budget denies → Failed, KQL never called | `TriageOrchestratorTests.cs` | `RunAsync_BudgetDenied_ReturnsFailedAndNoMcpCall` | 421 |
| 3 | Scope evaluator denies → Failed, strict no-KQL | `KqlGovernedEvidenceIntegrationTests.cs` | `WorkspaceScopeDeny_FailsBeforeKql` | 76 |
| 4 | Scope evaluator allows → KQL runs, Completed | `KqlGovernedEvidenceIntegrationTests.cs` | `WorkspaceScopeAllow_ProceedsToKqlAndReturnsCompleted` | 30 |
| 5 | Ok=true → Citations populated, Completed | `TriageOrchestratorTests.cs` | `RunAsync_Success_ReturnsCompletedWithCitations` | 34 |
| 6 | KQL throws, tool call still persisted → Degraded | `TriageOrchestratorTests.cs` | `RunAsync_ToolThrows_ReturnsDegradedAndStillPersistsToolCall` | 109 |
| 7 | Ok=false → Degraded | `TriageOrchestratorTests.cs` | `RunAsync_ToolReturnsNotOk_ReturnsDegradedStatus` | 178 |
| 8 | Success → Info logs only, no Warning | `TriageOrchestratorTests.cs` | `RunAsync_Success_LogsStartAndCompletion` | 236 |
| 9 | Throws → Warning log, no completion log | `TriageOrchestratorTests.cs` | `RunAsync_ToolThrows_LogsStartAndWarning` | 285 |
| 10 | `UnauthorizedAccessException` + IsDegraded=false → Failed (not Degraded) | `TriageOrchestratorTests.cs` | `RunAsync_ToolThrows_DegradedPolicyMapsToFailedStatus` | 479 |

### Additional Coverage (beyond required 10)

| Test File | Tests | Notable Scenarios |
|---|---|---|
| `TriageOrchestratorTests.cs` | 21 `[Fact]` methods | Runbook allowlist deny, runbook budget deny, runbook throws (partial degradation), both tools succeed, summary JSON validation |
| `KqlGovernedEvidenceIntegrationTests.cs` | 2 | Full Guardrail 2.5 integration (scope allow + scope deny) |
| `McpStdioKqlToolClientIntegrationTests.cs` | 3 | Input validation: invalid GUID, empty KQL, invalid timespan → Ok=false |
| `KqlToolContractTests.cs` | 4 | MCP contract: tool list schema check, validation on all three bad-input paths |

### Total Slice 57.1 Test Count

| Test Assembly | Count |
|---|---|
| `TriageOrchestratorTests.cs` | 21 |
| `KqlGovernedEvidenceIntegrationTests.cs` | 2 |
| `McpStdioKqlToolClientIntegrationTests.cs` | 3 |
| `KqlToolContractTests.cs` | 4 |
| **TOTAL** | **30** |

Requirement: ≥10 tests. **30 delivered ✅ (3× requirement)**

---

## Build + Test Gate Results

| Gate | Command | Result |
|---|---|---|
| Build (warnings as errors) | `dotnet build OpsCopilot.sln -warnaserror` | ✅ 0 errors, 0 warnings |
| Full test suite | `dotnet test OpsCopilot.sln --no-build -c Release` | ✅ 951/951 passing, 0 failures |

---

## Acceptance Criteria Verification

| AC | Status |
|---|---|
| `TriageOrchestrator` calls `IKqlToolClient.ExecuteAsync` (not `Azure.Monitor.Query`) | ✅ |
| All 3 guardrails checked before KQL call | ✅ Allowlist → Budget → ScopeEvaluator |
| `ToolCall.Create(...)` + `AppendToolCallAsync` called on both success and exception paths | ✅ |
| Citations built from `KqlToolResponse` fields | ✅ `BuildCitation(response)` uses `response.WorkspaceId/ExecutedQuery/Timespan/ExecutedAtUtc` |
| `DegradedDecision.IsDegraded=false` maps to `Failed` (not `Degraded`) | ✅ Covered by test at line 479 |
| Timeout in `McpStdioKqlToolClient` returns `Ok=false` (never throws) | ✅ `OperationCanceledException when (!ct.IsCancellationRequested)` → `ErrorResponse` |
| Runbook governance: deny → `Completed` with empty runbook citations (KQL citations preserved) | ✅ Tests at lines 542, 584, 634 |
| `Azure.Monitor.Query` has zero references outside `McpHost` | ✅ Boundary enforced |
| ≥10 tests covering allow/deny/timeout/citation/scope-block paths | ✅ 30 tests delivered |
