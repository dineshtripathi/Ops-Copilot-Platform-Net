# Slice 104 Evidence — SafeActions Telemetry Completeness + Rollback-Approve Bug Fix

## Objective

1. **Bug fix**: `rollback/approve` endpoint silently returned HTTP 500 when `ValidateAndNormalizeApprovalReason` threw `ArgumentException` for generic reasons (e.g. "lgtm"). Now returns 400 BadRequest.
2. **Telemetry completeness**: SafeActions lacked lifecycle counters for proposal creation and rollback requests. Three new counters added.

---

## Constraints Respected

- No new HTTP routes
- No schema changes or migrations
- No breaking DTO changes
- No secrets in logs
- No config keys invented
- `ValidateAndNormalizeApprovalReason` made non-static (private → instance) to access `_telemetry` — does not change any public API

---

## Changes

### `ISafeActionsTelemetry.cs`
Added three new methods after `RecordExecutionThrottled`:
```csharp
void RecordProposed(string actionType, string tenantId);
void RecordRollbackRequested(string actionType, string tenantId);
void RecordApprovalReasonRejected(string operationKind);
```

### `SafeActionsTelemetry.cs`
Three new `Counter<long>` fields initialized in ctor:

| Field | Metric name |
|---|---|
| `_proposalRecorded` | `safeactions.proposal.recorded` |
| `_rollbackRequested` | `safeactions.rollback.requested` |
| `_approvalReasonRejected` | `safeactions.approval.reason_rejected` |

Tags: `action_type` + `tenant_id` on proposal/rollback; `operation_kind` on reason rejected.

### `SafeActionOrchestrator.cs`
Six edits:
1. `ProposeAsync` — `_telemetry.RecordProposed(actionType, tenantId)` after `CreateActionRecordAsync` succeeds
2. `RequestRollbackAsync` — `_telemetry.RecordRollbackRequested(record.ActionType, record.TenantId)` after `SaveAsync`
3. `ApproveAsync` call site — `ValidateAndNormalizeApprovalReason(reason, "approve")`
4. `RejectAsync` call site — `ValidateAndNormalizeApprovalReason(reason, "reject")`
5. `ApproveRollbackAsync` call site — `ValidateAndNormalizeApprovalReason(reason, "rollback_approve")`
6. `ValidateAndNormalizeApprovalReason` — made non-static, added `operationKind` param, calls `_telemetry.RecordApprovalReasonRejected(operationKind)` before each throw

### `SafeActionEndpoints.cs`
`/{id:guid}/rollback/approve` endpoint:
- Added `catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }` between `KeyNotFoundException` and `InvalidOperationException` catches  
- Added `.ProducesProblem(StatusCodes.Status400BadRequest)` to endpoint metadata

---

## Tests Added

### `SafeActionsTelemetryTests.cs` (5 new)
| Test | Asserts |
|---|---|
| `ProposeAsync_Success_RecordsProposed` | `RecordProposed("restart_pod", "t-1")` called once |
| `RequestRollbackAsync_Success_RecordsRollbackRequested` | `RecordRollbackRequested("restart_pod", "t-1")` called once |
| `ApproveAsync_GenericReason_RecordsApprovalReasonRejected` | `RecordApprovalReasonRejected("approve")` called once on throw |
| `RejectAsync_GenericReason_RecordsApprovalReasonRejected` | `RecordApprovalReasonRejected("reject")` called once on throw |
| `ApproveRollbackAsync_GenericReason_RecordsApprovalReasonRejected` | `RecordApprovalReasonRejected("rollback_approve")` called once on throw |

### `SafeActionIdentityEndpointTests.cs` (1 new)
| Test | Asserts |
|---|---|
| `RollbackApprove_Returns400_ForGenericReason` | HTTP 400 for "lgtm" reason; repo never consulted |

---

## Build / Test Gate

```
dotnet build tests\Modules\SafeActions\...\csproj -warnaserror  → 0 warnings, 0 errors
dotnet test  tests\Modules\SafeActions\...\csproj --no-build    → 376 passed, 0 failed
```

> Full solution build blocked by `OpsCopilot.ApiHost (69584)` process holding file locks (pre-existing infra condition — not related to this slice).
