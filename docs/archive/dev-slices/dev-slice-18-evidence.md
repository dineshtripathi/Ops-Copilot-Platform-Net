# Dev Slice 18 — SafeActions Execution Observability (STRICT)

## Objective

Add structured observability for SafeActions execution and approval flows:
consistent structured logging, lightweight metrics/counters, endpoint-level and
orchestrator-level visibility for guard/deny/conflict/success paths.

**Observability only** — must not change business behavior, routing, schema, or
API contracts.

---

## Files Changed / Created

### New Files

| File | Layer | Purpose |
|------|-------|---------|
| `src/Modules/SafeActions/Application/…/Abstractions/ISafeActionsTelemetry.cs` | Application | Telemetry abstraction — 10 counter methods |
| `src/Modules/SafeActions/Presentation/…/Telemetry/SafeActionsTelemetry.cs` | Presentation | Implementation — `System.Diagnostics.Metrics.Meter("OpsCopilot.SafeActions", "1.0.0")`, 10 `Counter<long>`, `IDisposable` |
| `tests/Modules/SafeActions/…/SafeActionsTelemetryTests.cs` | Tests | 8 tests covering orchestrator telemetry paths |

### Modified Files

| File | Change |
|------|--------|
| `SafeActionOrchestrator.cs` | Added `ISafeActionsTelemetry` ctor param + 10 telemetry calls across Execute/Rollback flows |
| `SafeActionEndpoints.cs` | Added `[FromServices] ISafeActionsTelemetry` to 7 handlers + 10 telemetry calls |
| `SafeActionsPresentationExtensions.cs` | DI: `AddSingleton<ISafeActionsTelemetry, SafeActionsTelemetry>()` |
| `SafeActionOrchestratorTests.cs` | `CreateOrchestrator` helper — added optional `telemetry` param |
| `SafeActionTenantExecutionPolicyEndpointTests.cs` | Added `Mock.Of<ISafeActionsTelemetry>()` in 3 host setups |
| `SafeActionExecutionGuardTests.cs` | Added `Mock.Of<ISafeActionsTelemetry>()` in DI |
| `SafeActionDryRunEndpointTests.cs` | Added `Mock.Of<ISafeActionsTelemetry>()` in DI |
| `SafeActionRoutingEndpointTests.cs` | Added `Mock.Of<ISafeActionsTelemetry>()` in DI |
| `SafeActionIdentityEndpointTests.cs` | Added `Mock.Of<ISafeActionsTelemetry>()` in 2 DI locations |
| `SafeActionQueryEndpointTests.cs` | Added `Mock.Of<ISafeActionsTelemetry>()` in DI |
| `SafeActionDetailAuditEndpointTests.cs` | Added `Mock.Of<ISafeActionsTelemetry>()` in DI |
| `OpsCopilot.Api.http` | Appended Section T — 5 observability smoke requests |

---

## Meter & Counters

**Meter**: `OpsCopilot.SafeActions` v`1.0.0`

| Counter Name | Tags | Fires In |
|---|---|---|
| `safe_actions.execution.attempts` | `action_type`, `tenant_id` | Orchestrator: after record load |
| `safe_actions.execution.successes` | `action_type`, `tenant_id` | Orchestrator: execution success branch |
| `safe_actions.execution.failures` | `action_type`, `tenant_id` | Orchestrator: execution failure branch |
| `safe_actions.guarded_501` | `action_type`, `tenant_id` | Endpoint: 501 guard path |
| `safe_actions.replay_conflict` | `action_type`, `tenant_id` | Orchestrator: replay guard throw |
| `safe_actions.policy_denied` | `action_type`, `tenant_id` | Orchestrator: tenant-policy deny throw |
| `safe_actions.identity_missing_401` | `endpoint` | Endpoint: null-identity early return |
| `safe_actions.approval.decisions` | `action_type`, `tenant_id`, `decision` | Endpoint: approve/reject |
| `safe_actions.query.requests` | `query_kind` | Endpoint: GET list/detail |
| `safe_actions.query.validation_failures` | `query_kind` | Endpoint: query validation error |

---

## STRICT Conformance

| # | Rule | Status |
|---|------|--------|
| 1 | No behaviour changes — telemetry fire-and-forget | PASS |
| 2 | No route / DTO / schema changes | PASS |
| 3 | No new NuGet packages (`System.Diagnostics.Metrics` is BCL) | PASS |
| 4 | No double-counting — each counter fires once per logical path | PASS |
| 5 | No secrets or PII in tags | PASS |
| 6 | Build: 0 errors, 0 warnings (42 projects) | PASS |
| 7 | All 356 existing tests green (364 total = 356 + 8 new, 0 failures) | PASS |
| 8 | XML doc on every new public type | PASS |
| 9 | Observability impl in Presentation layer | PASS |
| 10 | Telemetry abstraction in Application layer | PASS |

---

## Test Results

```
Total tests: 364
  AgentRuns     :  53  ✅
  SafeActions   : 279  ✅   (271 existing + 8 new)
  Integration   :  24  ✅
  MCP Contract  :   8  ✅
Failures: 0
```

---

## .http Smoke Requests (Section T)

| # | Verb | Path | Expected |
|---|------|------|----------|
| T.1 | POST | `/safe-actions` | 201 — `attempts` counter fires |
| T.2 | POST | `/safe-actions/{id}/execute` | 200/501 — success/guard counters |
| T.3 | POST | `/safe-actions/{id}/approve` | 200 — `approval.decisions` counter |
| T.4 | GET  | `/safe-actions` | 200 — `query.requests` counter |
| T.5 | GET  | `/safe-actions/{id}` | 200 — `query.requests` counter |

---

## How to Verify Locally

```bash
# Build
dotnet build OpsCopilot.sln --no-incremental
# 0 errors, 0 warnings

# Test
dotnet test OpsCopilot.sln --no-build --verbosity normal
# 364 tests, 0 failures

# Metrics (optional, with dotnet-counters)
dotnet-counters monitor --counters OpsCopilot.SafeActions -p <PID>
```
