# Dev Slice 19.1 — SafeActions Naming Normalization (STRICT, No Behavior Change)

## Overview

Surgical normalization patch applied to Slice 19 (Execution Throttling) to align
429 response JSON body property names and telemetry counter naming with project conventions.
No behavior changes, no routing changes, no new packages, no schema changes.

---

## Acceptance-Criteria Evidence

| AC | Criterion | Status | Evidence |
|----|-----------|--------|----------|
| AC-1 | 429 JSON body property `error` renamed to `reasonCode` | ✅ | `SafeActionEndpoints.cs` — both execute and rollback/execute endpoints |
| AC-2 | `reasonCode` value is literal `"throttled"` (not `decision.ReasonCode`) | ✅ | `new { reasonCode = "throttled", ... }` in both endpoints |
| AC-3 | 429 JSON body property order is `reasonCode, message, retryAfterSeconds` | ✅ | Anonymous object property order in both endpoints |
| AC-4 | 429 body still has exactly 3 properties, status code still 429 | ✅ | Verified by `Execute_429Body_HasExactThreeProperties` test |
| AC-5 | Telemetry counter renamed from `safeactions.execution.throttled` to `safe_actions.execution.throttled` | ✅ | `SafeActionsTelemetry.cs` line 75 |
| AC-6 | `ISafeActionsTelemetry` doc comment updated to `safe_actions.execution.throttled` | ✅ | `ISafeActionsTelemetry.cs` line 41 |
| AC-7 | Endpoint integration tests updated for `reasonCode` property name and `"throttled"` value | ✅ | `SafeActionExecutionThrottleEndpointTests.cs` — 3 assertion sites updated |
| AC-8 | `.http` Section U comment updated to `{ reasonCode, message, retryAfterSeconds }` | ✅ | `docs/http/OpsCopilot.Api.http` line 993 |
| AC-9 | Slice 19 evidence doc has normalization addendum | ✅ | `docs/dev-slice-19-evidence.md` — appended addendum table |
| AC-10 | All 384 tests pass, 0 failures, 0 warnings | ✅ | 53 AgentRuns + 299 SafeActions + 24 Integration + 8 MCP Contract |

---

## Files Changed

| File | Change |
|------|--------|
| `src/Modules/SafeActions/Presentation/.../Endpoints/SafeActionEndpoints.cs` | `error` → `reasonCode = "throttled"`, property order normalized (×2 endpoints) |
| `src/Modules/SafeActions/Presentation/.../Telemetry/SafeActionsTelemetry.cs` | Counter name `safeactions.execution.throttled` → `safe_actions.execution.throttled` |
| `src/Modules/SafeActions/Application/.../Abstractions/ISafeActionsTelemetry.cs` | Doc comment updated |
| `tests/.../SafeActionExecutionThrottleEndpointTests.cs` | 3 assertion sites: `"error"` → `"reasonCode"`, `"TooManyRequests"` → `"throttled"` |
| `docs/http/OpsCopilot.Api.http` | Section U comment updated |
| `docs/dev-slice-19-evidence.md` | Normalization addendum appended |
| `docs/dev-slice-19.1-evidence.md` | This file |

---

## Before / After Contract Table

| Property | Before (Slice 19) | After (Slice 19.1) |
|----------|--------------------|---------------------|
| 429 JSON key 1 | `error` (value: `decision.ReasonCode`) | `reasonCode` (value: literal `"throttled"`) |
| 429 JSON key 2 | `retryAfterSeconds` | `message` |
| 429 JSON key 3 | `message` | `retryAfterSeconds` |
| Telemetry counter | `safeactions.execution.throttled` | `safe_actions.execution.throttled` |
| HTTP status code | `429` | `429` (unchanged) |
| Content-Type | `application/json` | `application/json` (unchanged) |

---

## No-Behavior-Change Confirmation

- ✅ HTTP status code unchanged (429)
- ✅ Content-Type unchanged (`application/json`)
- ✅ Property count unchanged (exactly 3)
- ✅ Property values semantically equivalent
- ✅ No routing changes
- ✅ No new packages
- ✅ No schema/migration changes
- ✅ No MCP/Worker host changes
- ✅ No executor routing changes
- ✅ Throttle check ordering unchanged (501 → record lookup → evaluate → 429/execute)

---

## Build & Test Totals

| Suite | Count | Status |
|-------|-------|--------|
| AgentRuns | 53 | ✅ Pass |
| SafeActions | 299 | ✅ Pass |
| Integration | 24 | ✅ Pass |
| MCP Contract | 8 | ✅ Pass |
| **Total** | **384** | **0 failures** |

Build: 0 warnings, 0 errors.
