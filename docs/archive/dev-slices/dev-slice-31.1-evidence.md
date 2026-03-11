# Slice 31.1 — SafeActions Governance Denial Normalization + Real Budget Enforcement — Evidence

## Overview

Slice 31.1 introduces `GovernanceDenialMapper` to produce **frozen, deterministic
reason codes** and **structured messages** for all governance denial paths. It also adds
**real MaxTokens enforcement** — even when the budget says "allowed", the orchestrator
now rejects payloads that exceed the `MaxTokens` cap returned by the budget decision.

**Constraints honoured:**

| Constraint | Status |
|---|---|
| No new routes | ✅ No routes added |
| No schema / migrations | ✅ No DB changes |
| No DTO breaking changes | ✅ Existing DTOs unchanged |
| No executor routing changes | ✅ `IActionExecutor` untouched |
| No changes to Governance policy implementations | ✅ Consume-only |
| Preserve existing safeguards | ✅ All prior checks retained |

---

## Deliverable A — `GovernanceDenialMapper`

**File:** `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/GovernanceDenialMapper.cs`

Static helper that maps raw governance decisions to `PolicyDeniedException` with frozen
reason codes and structured messages:

```csharp
public static class GovernanceDenialMapper
{
    public static PolicyDeniedException ToolDenied(PolicyDecision decision)
        => new("governance_tool_denied",
               $"Denied by governance tool allowlist (policyReason={decision.ReasonCode}): {decision.Message}");

    public static PolicyDeniedException BudgetDenied(BudgetDecision decision, int requestedTokens)
        => new("governance_budget_exceeded",
               $"Denied by governance token budget (policyReason={decision.ReasonCode}, requestedTokens={requestedTokens}, maxTokens={decision.MaxTokens?.ToString() ?? "null"}): {decision.Message}");
}
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| Frozen reason codes (`governance_tool_denied`, `governance_budget_exceeded`) | Downstream consumers can pattern-match on stable codes regardless of upstream changes |
| Raw `policyReason` embedded in message text | Preserves upstream diagnostic info without leaking it into structured fields |
| `requestedTokens` and `maxTokens` in budget message | Enables token-level debugging without additional logging |
| Static class, no dependencies | Pure mapping logic — no DI needed, trivially testable |

---

## Deliverable B — SafeActionOrchestrator Updates

**File:** `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/SafeActionOrchestrator.cs`

### Mapper Call Replacements (5 sites)

Every inline `throw new PolicyDeniedException(decision.ReasonCode, decision.Message)` was
replaced with `throw GovernanceDenialMapper.ToolDenied(decision)` or
`throw GovernanceDenialMapper.BudgetDenied(decision, requestedTokens)`:

| Method | Deny Type | Before | After |
|---|---|---|---|
| `ProposeAsync` | Tool | inline throw | `GovernanceDenialMapper.ToolDenied(govToolDecision)` |
| `ExecuteAsync` | Tool | inline throw | `GovernanceDenialMapper.ToolDenied(govToolDecision)` |
| `ExecuteAsync` | Budget | inline throw | `GovernanceDenialMapper.BudgetDenied(govBudgetDecision, requestedTokens)` |
| `ExecuteRollbackAsync` | Tool | inline throw | `GovernanceDenialMapper.ToolDenied(govToolDecision)` |
| `ExecuteRollbackAsync` | Budget | inline throw | `GovernanceDenialMapper.BudgetDenied(govBudgetDecision, rollbackTokens)` |

### MaxTokens Enforcement Blocks (2 new)

When `BudgetDecision.Allowed == true` but `requestedTokens > MaxTokens`, the orchestrator
now denies the operation. This catches scenarios where the budget service approves the
action but caps the token count below what the payload requires.

**ExecuteAsync** (after budget check):
```csharp
if (govBudgetDecision.MaxTokens is not null && requestedTokens > govBudgetDecision.MaxTokens.Value)
{
    _telemetry.RecordPolicyDenied(tenantId, actionType, "governance_budget_exceeded");
    throw GovernanceDenialMapper.BudgetDenied(govBudgetDecision, requestedTokens);
}
```

**ExecuteRollbackAsync** (after budget check):
```csharp
if (govBudgetDecision.MaxTokens is not null && rollbackTokens > govBudgetDecision.MaxTokens.Value)
{
    _telemetry.RecordPolicyDenied(tenantId, actionType, "governance_budget_exceeded");
    throw GovernanceDenialMapper.BudgetDenied(govBudgetDecision, rollbackTokens);
}
```

---

## Deliverable C — Test Updates (11 tests modified/added)

**File:** `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/SafeActionOrchestratorTests.cs`

### Existing Tests Updated with Message Assertions (6 tests)

Added `Assert.Contains` checks on the structured message format to existing governance
denial tests:

| # | Test Name | Assertions Added |
|---|---|---|
| 1 | `ProposeAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | `"Denied by governance tool allowlist"`, `"policyReason=TOOL_NOT_ALLOWED"` in message |
| 2 | `ExecuteAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | Same tool-deny message assertions |
| 3 | `ExecuteRollbackAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | Same tool-deny message assertions |
| 4 | `ExecuteRollbackAsync_Throws_PolicyDenied_Null_Payload_Check_After_Tool_Deny` | Same tool-deny message assertions |
| 5 | `ExecuteAsync_Throws_PolicyDenied_When_Governance_Budget_Exceeded` | `"Denied by governance token budget"`, `"policyReason=BUDGET_EXHAUSTED"`, `"requestedTokens="`, `"maxTokens=null"` |
| 6 | `ExecuteRollbackAsync_Throws_PolicyDenied_When_Governance_Budget_Exceeded` | Same budget-deny message assertions |

### New Tests Added (5 tests)

| # | Test Name | Scenario | Key Assertions |
|---|---|---|---|
| 7 | `ExecuteAsync_Throws_PolicyDenied_When_Budget_Allowed_But_MaxTokens_Exceeded` | `BudgetDecision.Allow(1)`, payload needs 4 tokens | Frozen code `governance_budget_exceeded`, message contains `policyReason=ALLOWED`, `maxTokens=1` |
| 8 | `ExecuteAsync_MaxTokens_Enforcement_Records_Telemetry` | Same MaxTokens exceeded | `RecordPolicyDenied` called once with correct args |
| 9 | `ExecuteAsync_Budget_Allowed_Null_MaxTokens_Skips_Enforcement` | `BudgetDecision.Allow()` (MaxTokens=null) | Execution proceeds to `ActionStatus.Completed` — full happy path |
| 10 | `ExecuteAsync_Tool_Deny_Normalizes_Arbitrary_PolicyReason` | `PolicyDecision.Deny("CUSTOM_TOOL_BLOCK_42", "...")` | Frozen code `governance_tool_denied`, raw reason `CUSTOM_TOOL_BLOCK_42` preserved in message |
| 11 | `ExecuteRollbackAsync_Throws_PolicyDenied_When_Budget_Allowed_But_MaxTokens_Exceeded` | Full rollback lifecycle, `BudgetDecision.Allow(1)` | Same MaxTokens enforcement on rollback path |

**Total test count:** 55 `[Fact]` tests in SafeActions (50 pre-Slice 31.1 + 5 new).

---

## Deliverable D — .http Section AH (5 requests)

**File:** `docs/http/OpsCopilot.Api.http`

| # | Request | Scenario | Expected |
|---|---|---|---|
| AH1 | `POST /safe-actions/propose` (`forbidden_action`) | Tool not in allowlist | 400, `governance_tool_denied` |
| AH2 | `POST /safe-actions/{id}/execute` | Budget exhausted | 400, `governance_budget_exceeded` |
| AH3 | `POST /safe-actions/{id}/execute` | Budget allowed but MaxTokens=1 | 400, `governance_budget_exceeded`, `maxTokens=1` |
| AH4 | `POST /safe-actions/{id}/rollback` | Budget denied on rollback | 400, `governance_budget_exceeded` |
| AH5 | `POST /safe-actions/propose` (`custom_blocked_action`) | Arbitrary upstream policyReason | 400, frozen `governance_tool_denied`, raw reason in message |

TOC updated with entries for AG (Slice 31) and AH (Slice 31.1).

---

## Deliverable E — Build + Test

| Metric | Result |
|---|---|
| `dotnet build` warnings | **0** |
| `dotnet build` errors | **0** |
| Total tests | **593** (588 pre-Slice 31.1 + 5 new) |
| Test failures | **0** |
| Skipped tests | **0** |
---

## Reconciliation Patch (post-commit 1f67002)

Fixes documentation/test discrepancies found after initial commit — **no production code changes**.

### Changes Applied

| # | File | What changed | Lines affected |
|---|---|---|---|
| B | `src/Hosts/OpsCopilot.ApiHost/OpsCopilot.Api.http` (Section AH) | Expected status comments `422` → `400` | 6 comment lines |
| C | `docs/dev-slice-31.1-evidence.md` | Deliverable C: policyReason values corrected; Deliverable D: `422` → `400` | 2 tables |
| D | `tests/.../SafeActionOrchestratorTests.cs` | Mock upstream reasons changed from frozen codes to realistic raw codes; policyReason assertions updated | 14 mock + 6 assertion sites |

### Verification

| Metric | Result |
|---|---|
| `dotnet build` warnings | **0** |
| `dotnet build` errors | **0** |
| Total tests | **593** (unchanged) |
| Test failures | **0** |
| Skipped tests | **0** |
| SafeActions tests | **341 passed** |