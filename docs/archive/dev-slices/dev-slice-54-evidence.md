# Dev Slice 54 — Mode C Execution Groundwork: Approvals + Idempotency + Throttling

## Summary

Pre-flight inspection confirmed all Slice 54 features are fully implemented and tested.
No production code or test changes were required — the guard chain, throttling, replay
idempotency, governance enforcement, and telemetry were all already in place and covered
by the existing test suite.

---

## Pre-flight Findings

All components inspected and confirmed complete:

| Component | Location | Status |
|---|---|---|
| Kill-switch guard (→501) | `SafeActionEndpoints.cs` | ✅ Implemented |
| Execution throttle (→429) | `SafeActionEndpoints.cs` | ✅ Implemented |
| `IExecutionThrottlePolicy` interface | `Application/Abstractions/` | ✅ Implemented |
| `InMemoryExecutionThrottlePolicy` | `Infrastructure/` | ✅ Implemented |
| Throttle DI registration (singleton) | `SafeActionsPresentationExtensions.cs` | ✅ Registered |
| `Retry-After` header on 429 | `SafeActionEndpoints.cs` | ✅ Implemented |
| Tenant execution policy gate | `SafeActionOrchestrator.cs` | ✅ Implemented |
| Governance tool allowlist (Execute) | `SafeActionOrchestrator.cs` | ✅ Implemented |
| Governance token budget (Execute) | `SafeActionOrchestrator.cs` | ✅ Implemented |
| Governance tool allowlist (ExecuteRollback) | `SafeActionOrchestrator.cs` | ✅ Implemented |
| Governance token budget (ExecuteRollback) | `SafeActionOrchestrator.cs` | ✅ Implemented |
| Replay guard — all non-Approved states (Execute) | `SafeActionOrchestrator.cs` | ✅ Implemented |
| Replay guard — Executing state | `SafeActionOrchestrator.cs` | ✅ `InvalidOperationException` |
| Replay guard — ExecuteRollback | `SafeActionOrchestrator.cs` | ✅ Implemented |
| `ISafeActionsTelemetry` — all 12 methods | `Application/Abstractions/` | ✅ Complete |
| `SafeActionsTelemetry` implementation | `Presentation/Telemetry/` | ✅ Complete |
| Telemetry DI registration (singleton) | `SafeActionsPresentationExtensions.cs` | ✅ Registered |

---

## Guard Chain Architecture (confirmed)

### Endpoint layer (both `/execute` and `/rollback/execute`)

```
1. Kill-switch: SafeActions:EnableExecution == false → 501 Not Implemented
2. Load record → 404 if not found
3. IExecutionThrottlePolicy.Evaluate() denied → 429 Too Many Requests
   + Retry-After header set
4. orchestrator.ExecuteAsync() / ExecuteRollbackAsync()
   ├── PolicyDeniedException          → 400 Bad Request
   ├── InvalidOperationException      → 409 Conflict
   └── KeyNotFoundException           → 404 Not Found
```

### Orchestrator layer

```
ExecuteAsync guard order:
1. Tenant execution policy        → PolicyDeniedException("tenant_not_authorized_for_action")
2. Governance tool allowlist       → PolicyDeniedException("governance_tool_denied")
3. Governance token budget         → PolicyDeniedException("governance_budget_exceeded")
4. Replay guard (status != Approved) → InvalidOperationException → 409

ExecuteRollbackAsync guard order:
1. Tenant execution policy        → PolicyDeniedException("tenant_not_authorized_for_action")
2. Governance tool allowlist       → PolicyDeniedException("governance_tool_denied")
3. Governance token budget         → PolicyDeniedException("governance_budget_exceeded")
4. Replay guard (status != RollbackApproved) → InvalidOperationException → 409
```

---

## Test Coverage Verified (SafeActionOrchestratorTests.cs)

### Kill-switch + throttle
- Kill-switch returns 501, records `RecordGuarded501` telemetry
- Throttle returns 429 with `Retry-After` header, records `RecordExecutionThrottled` telemetry
- Rollback execute: same kill-switch + throttle patterm with `"rollback_execute"` operationKind

### Replay guard — ExecuteAsync (all 4 non-Approved states)
| Test | State | Expected |
|---|---|---|
| `ExecuteAsync_ReplayGuard_Throws_When_Already_Completed` | Completed | `InvalidOperationException` |
| `ExecuteAsync_ReplayGuard_Throws_When_Already_Failed` | Failed | `InvalidOperationException` |
| `ExecuteAsync_ReplayGuard_Throws_When_Already_Executing` | Executing | `InvalidOperationException` |
| `ExecuteAsync_ReplayGuard_Throws_When_Rejected` | Rejected | `InvalidOperationException` |

### Replay guard — ExecuteRollbackAsync
| Test | State | Expected |
|---|---|---|
| `ExecuteRollbackAsync_ReplayGuard_Throws_When_Already_RolledBack` | RolledBack | `InvalidOperationException` |
| `ExecuteRollbackAsync_ReplayGuard_Throws_When_RollbackFailed` | RollbackFailed | `InvalidOperationException` |

### Governance — ExecuteAsync
| Test | Scenario |
|---|---|
| `ExecuteAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | reasonCode=`governance_tool_denied` |
| `ExecuteAsync_Throws_PolicyDenied_When_Governance_Budget_Exceeded` | reasonCode=`governance_budget_exceeded` |
| `ExecuteAsync_Throws_PolicyDenied_When_Budget_Allowed_But_MaxTokens_Exceeded` | MaxTokens=1 enforcement |
| `ExecuteAsync_Governance_Budget_Enforcement_Records_Telemetry` | `RecordPolicyDenied` called ×1 |
| `ExecuteAsync_Governance_Both_Allowed_Proceeds_To_Execute` | MaxTokens=8192, → Completed |
| `ExecuteAsync_Governance_Tool_Check_Runs_Before_Budget_Check` | callOrder=["tool"] only |
| `ExecuteAsync_Budget_Allowed_Null_MaxTokens_Skips_Enforcement` | null MaxTokens → no exception |
| `ExecuteAsync_Tool_Deny_Normalizes_Arbitrary_PolicyReason` | any raw reason → `governance_tool_denied` |
| `ExecuteAsync_Governance_Budget_Denied_Records_Telemetry` | `RecordPolicyDenied` called ×1 |
| `ExecuteAsync_Governance_Budget_Uses_Correct_Token_Count` | `min(8192, payloadLen/4)` |

### Governance — ExecuteRollbackAsync
| Test | Scenario |
|---|---|
| `ExecuteRollbackAsync_Throws_PolicyDenied_When_Governance_Tool_Denied` | reasonCode=`governance_tool_denied` |
| `ExecuteRollbackAsync_Throws_PolicyDenied_When_Governance_Budget_Exceeded` | reasonCode=`governance_budget_exceeded` |
| `ExecuteRollbackAsync_Throws_PolicyDenied_When_Budget_Allowed_But_MaxTokens_Exceeded` | MaxTokens=1 |
| `ExecuteRollbackAsync_Governance_Budget_Denied_Records_Telemetry` | `RecordPolicyDenied` called ×1 |
| `ExecuteRollbackAsync_Governance_Both_Allowed_Proceeds_To_Rollback` | → RolledBack |
| `ExecuteRollbackAsync_Governance_Tool_Check_Runs_Before_Null_Payload_Check` | tool denial beats null-payload guard |
| `ExecuteRollbackAsync_Governance_Budget_Uses_Correct_Token_Count` | `min(8192, rollbackPayloadLen/4)` |

### Tenant policy gate
| Test | Path |
|---|---|
| `ExecuteAsync_TenantPolicy_Throws_PolicyDenied` | ExecuteAsync → `tenant_not_authorized_for_action` |
| `ExecuteRollbackAsync_TenantPolicy_Throws_PolicyDenied` | ExecuteRollbackAsync → `tenant_not_authorized_for_action` |

---

## Configuration Keys (InMemoryExecutionThrottlePolicy)

| Key | Default | Purpose |
|---|---|---|
| `SafeActions:EnableExecution` | `false` | Kill-switch; off by default (safe) |
| `SafeActions:EnableExecutionThrottling` | `true` | Toggle throttle enforcement |
| `SafeActions:ExecutionThrottleWindowSeconds` | `60` | Fixed-window duration |
| `SafeActions:ExecutionThrottleMaxAttemptsPerWindow` | `5` | Max calls per tenant+actionType per window |

---

## Build

```
dotnet build OpsCopilot.sln -warnaserror
```

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Tests

```
dotnet test OpsCopilot.sln --no-build -q
```

| Assembly | Passed |
|---|---|
| OpsCopilot.Modules.Connectors.Tests | 30 |
| OpsCopilot.Modules.Governance.Tests | 31 |
| OpsCopilot.Modules.Evaluation.Tests | 15 |
| OpsCopilot.Modules.AlertIngestion.Tests | 31 |
| OpsCopilot.Modules.Reporting.Tests | 27 |
| OpsCopilot.Modules.AgentRuns.Tests | 81 |
| OpsCopilot.Modules.Tenancy.Tests | 17 |
| OpsCopilot.Modules.Packs.Tests | 303 |
| OpsCopilot.Modules.SafeActions.Tests | 368 |
| OpsCopilot.Integration.Tests | 24 |
| OpsCopilot.Mcp.ContractTests | 8 |
| **Grand Total** | **935** |

**Failed: 0 / Skipped: 0**

Baseline maintained. No net change (all features were already implemented in prior slices).

---

## Files Changed

| File | Change |
|---|---|
| `docs/dev-slice-54-evidence.md` | Created — this document |

No production code or test files were modified. All Slice 54 features confirmed present
via pre-flight inspection of the full guard chain.
