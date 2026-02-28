# Slice 31 — SafeActions Governance Enforcement — Evidence

## Overview

Slice 31 integrates governance policy checks (tool allowlist + token budget) into the
SafeActions module so that every propose / execute / rollback call is gated by the
Governance module's policy engine.

**Constraints honoured:**

| Constraint | Status |
|---|---|
| No new routes | ✅ No routes added |
| No schema / migrations | ✅ No DB changes |
| No DTO breaking changes | ✅ Existing DTOs unchanged |
| No executor routing changes | ✅ `IActionExecutor` untouched |
| No changes to Governance policy implementations | ✅ Consume-only via `IGovernancePolicyClient` |
| Preserve existing safeguards | ✅ All prior checks retained |

---

## Deliverable A — `IGovernancePolicyClient` Abstraction

**File:** `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Governance/IGovernancePolicyClient.cs`

```csharp
public interface IGovernancePolicyClient
{
    PolicyDecision EvaluateToolAllowlist(string tenantId, string actionType);
    BudgetDecision EvaluateTokenBudget(string tenantId, string actionType, int? requestedTokens = null);
}
```

Returns existing contract types from `OpsCopilot.BuildingBlocks.Contracts.Governance`:
- `PolicyDecision(bool Allowed, string ReasonCode, string Message)`
- `BudgetDecision(bool Allowed, string ReasonCode, string Message, int? MaxTokens)`

---

## Deliverable B — `GovernancePolicyClient` Implementation + DI Wiring

**File:** `src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/Governance/GovernancePolicyClient.cs`

- Constructor receives `IToolAllowlistPolicy` and `ITokenBudgetPolicy` (Governance module contracts).
- `EvaluateToolAllowlist` → delegates to `IToolAllowlistPolicy.CanUseTool(tenantId, actionType)`.
- `EvaluateTokenBudget` → bridges to `ITokenBudgetPolicy.CheckRunBudget(tenantId, runId)`.
  - `runId` is deterministic: `new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}::{actionType}"))[..16])`.
  - `DefaultTokenBudgetPolicy` ignores `runId` anyway — this is a stable placeholder.

**DI Registration:** `SafeActionsInfrastructureExtensions.cs`
```csharp
services.AddScoped<IGovernancePolicyClient, GovernancePolicyClient>();
```

---

## Deliverable C — SafeActionOrchestrator Modifications

**File:** `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/SafeActionOrchestrator.cs`

Constructor now accepts 8 parameters (added `IGovernancePolicyClient governanceClient`):

```
IActionRecordRepository, IActionExecutor, ISafeActionPolicy, ITenantExecutionPolicy,
IActionTypeCatalog, ISafeActionsTelemetry, IGovernancePolicyClient, ILogger
```

### ProposeAsync

Check ordering: catalog → policy → **governance tool allowlist** → log → create record.

```csharp
var govToolDecision = _governanceClient.EvaluateToolAllowlist(tenantId, actionType);
if (!govToolDecision.Allowed)
{
    _telemetry.RecordPolicyDenied(tenantId, actionType, govToolDecision.ReasonCode);
    throw new PolicyDeniedException(govToolDecision.ReasonCode, govToolDecision.Message);
}
```

### ExecuteAsync

Check ordering: tenant policy → **governance tool allowlist** → **governance token budget** → replay guard → execute.

Token budget formula: `Math.Min(8192, record.ProposedPayloadJson.Length / 4)` — deterministic, no AI tokenizers.

### ExecuteRollbackAsync

Check ordering: tenant policy → **governance tool allowlist** → null payload check → **governance token budget** → replay guard → rollback.

Budget check placed AFTER null-payload check to avoid `NullReferenceException` on `RollbackPayloadJson.Length`.

### Reason Codes

| Code | Thrown when |
|---|---|
| `governance_tool_denied` | `EvaluateToolAllowlist` returns `Allowed = false` |
| `governance_budget_exceeded` | `EvaluateTokenBudget` returns `Allowed = false` |

---

## Deliverable D — Endpoint Behaviour Verification

**File:** `src/Modules/SafeActions/Presentation/OpsCopilot.SafeActions.Presentation/Endpoints/SafeActionEndpoints.cs`

All three endpoints (ProposeAction, ExecuteAction, ExecuteRollback) already catch
`PolicyDeniedException` with a generic pattern:

```csharp
catch (PolicyDeniedException ex)
{
    return Results.BadRequest(new { ex.ReasonCode, ex.Message });
}
```

This pattern is **reason-code-agnostic** — it automatically surfaces `governance_tool_denied`
and `governance_budget_exceeded` without any code changes. Catch blocks verified at lines 63,
340, and 471.

**Result:** No endpoint changes required.

---

## Deliverable E — Tests + .http Section AG

### Unit Tests (16 new)

**File:** `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/SafeActionOrchestratorTests.cs`

**Test helper update:** `CreateOrchestrator` now accepts optional `Mock<IGovernancePolicyClient>` (7th param)
with defaults: `EvaluateToolAllowlist → PolicyDecision.Allow()`, `EvaluateTokenBudget → BudgetDecision.Allow(8192)`.
Constructor call passes 8 arguments. All 30+ existing tests continue working unchanged.

#### ProposeAsync Governance (4 tests)

| # | Test Name | Asserts |
|---|---|---|
| 1 | `ProposeAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | Throws `PolicyDeniedException` with ReasonCode `governance_tool_denied` |
| 2 | `ProposeAsync_Governance_Tool_Denied_Never_Creates_Record` | Repository `CreateAsync` never called |
| 3 | `ProposeAsync_Governance_Tool_Denied_Records_Telemetry` | `RecordPolicyDenied` called with correct args |
| 4 | `ProposeAsync_Governance_Tool_Allowed_Proceeds_To_Create` | Repository `CreateAsync` called once |

#### ExecuteAsync Governance (6 tests)

| # | Test Name | Asserts |
|---|---|---|
| 5 | `ExecuteAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | Throws with `governance_tool_denied` |
| 6 | `ExecuteAsync_Throws_PolicyDenied_When_Governance_Budget_Exceeded` | Throws with `governance_budget_exceeded` |
| 7 | `ExecuteAsync_Governance_Budget_Denied_Records_Telemetry` | `RecordPolicyDenied` called |
| 8 | `ExecuteAsync_Governance_Both_Allowed_Proceeds_To_Execute` | `ExecuteAsync` on executor called |
| 9 | `ExecuteAsync_Governance_Tool_Check_Runs_Before_Budget_Check` | Tool deny → budget never called |
| 10 | `ExecuteAsync_Governance_Budget_Uses_Correct_Token_Count` | `requestedTokens == Math.Min(8192, payload.Length / 4)` |

#### ExecuteRollbackAsync Governance (6 tests)

| # | Test Name | Asserts |
|---|---|---|
| 11 | `ExecuteRollbackAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | Throws with `governance_tool_denied` |
| 12 | `ExecuteRollbackAsync_Throws_PolicyDenied_When_Governance_Budget_Exceeded` | Throws with `governance_budget_exceeded` |
| 13 | `ExecuteRollbackAsync_Governance_Budget_Denied_Records_Telemetry` | `RecordPolicyDenied` called |
| 14 | `ExecuteRollbackAsync_Governance_Both_Allowed_Proceeds_To_Rollback` | `ExecuteRollbackAsync` on executor called |
| 15 | `ExecuteRollbackAsync_Governance_Tool_Check_Runs_Before_Null_Payload_Check` | Tool deny → budget + null check never reached |
| 16 | `ExecuteRollbackAsync_Governance_Budget_Uses_Correct_Token_Count` | `requestedTokens == Math.Min(8192, rollbackPayload.Length / 4)` |

### .http Section AG (4 requests)

**File:** `docs/http/OpsCopilot.Api.http`

| # | Request | Purpose |
|---|---|---|
| AG1 | `POST /safe-actions/propose` (restart_pod) | Governance tool allow-listed → 200 Proposed |
| AG2 | Approve + Execute | Governance allows tool + budget → 200 Completed |
| AG3 | Full rollback lifecycle (6 requests) | Propose → approve → execute → request-rollback → approve-rollback → rollback |
| AG4 | `GET /safe-actions?tenantId=...&actionType=restart_pod` | Query governance audit trail |

---

## Deliverable G — Build + Test

| Metric | Result |
|---|---|
| `dotnet build` warnings | **0** |
| `dotnet build` errors | **0** |
| Total tests | **588** (572 pre-Slice 31 + 16 new) |
| Test failures | **0** |
| Skipped tests | **0** |

**Verified** — full solution build and test suite pass on `main`.
