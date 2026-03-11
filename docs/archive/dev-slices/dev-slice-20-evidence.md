# Slice 20 — SafeActions Throttle Retry-After Header + Observability Alignment

**Status**: COMPLETE  
**Baseline commit**: `8b58999` (slices 19 + 19.1)  
**Baseline tests**: 384 passing  
**Post-slice tests**: 392 passing (384 + 8 new)  

---

## Acceptance Criteria Checklist

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | Execute endpoint 429 response includes `Retry-After` header | ✅ |
| AC-2 | Rollback-execute endpoint 429 response includes `Retry-After` header | ✅ |
| AC-3 | `Retry-After` header value matches `retryAfterSeconds` in JSON body | ✅ |
| AC-4 | 200 OK responses do NOT include `Retry-After` header | ✅ |
| AC-5 | 501 guarded responses do NOT include `Retry-After` header | ✅ |
| AC-6 | `Retry-After` header set BEFORE `Results.Json(...)` return | ✅ |
| AC-7 | Frozen 429 JSON body unchanged: `{ reasonCode, message, retryAfterSeconds }` | ✅ |
| AC-8 | Structured `LogWarning` on execute throttle-deny path | ✅ |
| AC-9 | Structured `LogWarning` on rollback-execute throttle-deny path | ✅ |
| AC-10 | Warning log includes `{ActionType}`, `{TenantId}`, `{OperationKind}`, `{RetryAfterSeconds}` | ✅ |
| AC-11 | No new telemetry counters added | ✅ |
| AC-12 | Telemetry counter name `safe_actions.execution.throttled` unchanged | ✅ |
| AC-13 | No new routes, DTOs, or schema changes | ✅ |
| AC-14 | No new NuGet packages | ✅ |
| AC-15 | ≥8 new tests written and passing | ✅ (8 new) |
| AC-16 | All existing tests still pass (no regressions) | ✅ |
| AC-17 | .http documentation updated with Section V (`tenant-verify-20`) | ✅ |
| AC-18 | Evidence document created | ✅ (this file) |

---

## Files Changed

| File | Change Summary |
|------|---------------|
| `src/Modules/SafeActions/Presentation/OpsCopilot.SafeActions.Presentation/Endpoints/SafeActionEndpoints.cs` | Added `Retry-After` response header + `LogWarning` on both execute and rollback-execute 429 paths |
| `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/SafeActionExecutionThrottleEndpointTests.cs` | Added `CapturingLogger<T>`, extended `CreateTestHost`, added 8 new test methods |
| `docs/http/OpsCopilot.Api.http` | Added Section V (Retry-After header verification) with tenant `tenant-verify-20` |
| `docs/dev-slice-20-evidence.md` | This evidence document |

---

## New Tests (8)

| # | Test Name | Verifies |
|---|-----------|----------|
| 1 | `Execute_Throttled_HasRetryAfterHeader` | 429 from execute includes `Retry-After` header |
| 2 | `RollbackExecute_Throttled_HasRetryAfterHeader` | 429 from rollback-execute includes `Retry-After` header |
| 3 | `Execute_Throttled_RetryAfterHeaderMatchesBody` | Header value "45" matches body `retryAfterSeconds: 45` |
| 4 | `RollbackExecute_Throttled_RetryAfterHeaderMatchesBody` | Header value "30" matches body `retryAfterSeconds: 30` |
| 5 | `Execute_ThrottleAllows_NoRetryAfterHeader` | 200 OK has NO `Retry-After` header |
| 6 | `Execute_Guarded501_NoRetryAfterHeader` | 501 guarded has NO `Retry-After` header |
| 7 | `Execute_Throttled_LogsWarning` | Warning logged with ActionType, TenantId, OperationKind, RetryAfterSeconds |
| 8 | `RollbackExecute_Throttled_LogsWarning` | Same for rollback-execute path |

---

## Sample 429 Response

**Headers:**
```
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 45
```

**Body:**
```json
{
  "reasonCode": "throttled",
  "message": "Rate limit exceeded for restart_pod by t-throttle. Retry after 45s.",
  "retryAfterSeconds": 45
}
```

---

## Build & Test Results

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed!  -  Failed: 0, Passed:  53 - AgentRuns
Passed!  -  Failed: 0, Passed: 307 - SafeActions (299 existing + 8 new)
Passed!  -  Failed: 0, Passed:  24 - Integration
Passed!  -  Failed: 0, Passed:   8 - MCP Contract
                          Total: 392
```
