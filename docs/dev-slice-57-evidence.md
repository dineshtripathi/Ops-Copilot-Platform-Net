# Slice 57 — `ITargetScopeEvaluator` (Guardrail 2.5)

## Objective
Inject `ITargetScopeEvaluator` into `TriageOrchestrator` as an optional dependency and enforce
a workspace-scope check (Guardrail 2.5) between the budget guardrail and the KQL execution step.

---

## What Changed

### `TriageOrchestrator.cs`
- Added private field `ITargetScopeEvaluator? _scopeEvaluator`
- Added optional constructor parameter `ITargetScopeEvaluator? scopeEvaluator = null` (position 14, last)
- Added constructor assignment `_scopeEvaluator = scopeEvaluator`
- Inserted **Guardrail 2.5** block between budget-deny return and `// ── Execute KQL tool (MCP) ──`:
  - Null-guarded: no-op when `_scopeEvaluator` is `null` (all 20+ existing tests unchanged)
  - Calls `_scopeEvaluator.Evaluate(tenantId, "LogAnalyticsWorkspace", workspaceId)`
  - Appends a `AgentRunPolicyEvent` via `AppendPolicyEventAsync` regardless of allow/deny
  - On deny: logs warning, calls `CompleteRunAsync(…, AgentRunStatus.Failed, …)`, returns `TriageResult` with `Failed` status

### Guardrail order (final)
1. Allowlist check (Guardrail 1)
2. Token budget check (Guardrail 2)
3. **Workspace scope check (Guardrail 2.5)** ← NEW
4. KQL execution (MCP)

---

## Tests Added (3 new → total 951)

| # | Assembly | Test Name | Type |
|---|---|---|---|
| 1 | `AgentRuns.Tests` | `TriageOrchestratorTests.RunAsync_WorkspaceScopeDenied_ReturnsFailedAndNoKqlCall` | Unit |
| 2 | `AgentRuns.Tests` | `KqlGovernedEvidenceIntegrationTests.WorkspaceScopeAllow_ProceedsToKqlAndReturnsCompleted` | Integration (Moq) |
| 3 | `AgentRuns.Tests` | `KqlGovernedEvidenceIntegrationTests.WorkspaceScopeDeny_FailsBeforeKql` | Integration (Moq) |

### Test 1 — Unit: scope deny
- Strict `IKqlToolClient` and `IRunbookSearchToolClient` with **no setups** — any call throws
- `TargetScopeDecision.Deny("WORKSPACE_NOT_ALLOWED", …)` returned by strict `ITargetScopeEvaluator` mock
- Asserts `AgentRunStatus.Failed`
- Verifies `kqlMock.VerifyNoOtherCalls()` and `runbookMock.VerifyNoOtherCalls()`
- Verifies `AppendPolicyEventAsync(PolicyName == nameof(ITargetScopeEvaluator) && !Allowed && ReasonCode == "WORKSPACE_NOT_ALLOWED", Times.Once)`

### Test 2 — Integration: scope allow → proceeds to KQL
- `TargetScopeDecision.Allow()` returned by strict evaluator mock
- Full happy-path helpers (repo, kql, runbook, governance, session)
- Asserts `AgentRunStatus.Completed`
- Verifies `kqlMock.ExecuteAsync(Times.AtLeastOnce())`
- Verifies `AppendPolicyEventAsync(…Allowed == true…, Times.Once)`

### Test 3 — Integration: scope deny → fails before KQL
- `TargetScopeDecision.Deny("WORKSPACE_NOT_ALLOWED", …)` returned by strict evaluator mock
- Strict `IKqlToolClient` / `IRunbookSearchToolClient` with **no setups**
- Asserts `AgentRunStatus.Failed`
- Verifies no KQL or runbook calls

---

## Hard Constraints Maintained

| Constraint | Status |
|---|---|
| No new HTTP routes | ✅ None added |
| No DTO breaking changes | ✅ Constructor param added as optional last |
| No DB schema / migration changes | ✅ None |
| `Azure.Monitor.Query` not outside McpHost | ✅ Not introduced |
| No secrets in logs/docs/tests | ✅ Clean |
| Existing 20+ `TriageOrchestratorTests` unchanged | ✅ Null-guard ensures backward compat |
| No background workers / queues | ✅ None |

---

## Build Gate

```
dotnet build OpsCopilot.sln -warnaserror -c Release --no-incremental
```

Result: **Build succeeded. 0 Warning(s) 0 Error(s)**

## Test Gate

```
dotnet test OpsCopilot.sln -c Release --no-build
```

Results:

| Assembly | Passed |
|---|---|
| `OpsCopilot.Modules.Governance.Tests` | 31 |
| `OpsCopilot.Modules.Connectors.Tests` | 30 |
| `OpsCopilot.Modules.Evaluation.Tests` | 15 |
| `OpsCopilot.Modules.AlertIngestion.Tests` | 31 |
| `OpsCopilot.Modules.Reporting.Tests` | 27 |
| `OpsCopilot.Modules.AgentRuns.Tests` | **97** (+3) |
| `OpsCopilot.Modules.Tenancy.Tests` | 17 |
| `OpsCopilot.Modules.Packs.Tests` | 303 |
| `OpsCopilot.Modules.SafeActions.Tests` | 368 |
| `OpsCopilot.Integration.Tests` | 24 |
| `OpsCopilot.Mcp.ContractTests` | 8 |
| **Total** | **951** ✅ |

- Failed: **0** ✅
- Gate: ≥951 passing, 0 failures ✅ (baseline was 948)
