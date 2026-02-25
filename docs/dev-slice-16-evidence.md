# Dev Slice 16 — SafeActions Action Detail Audit Enrichment — Evidence

## Slice Summary

**Goal**: Enhance `GET /safe-actions/{id}` to return full structured approval
history and execution log history collections, with sensitive payload redaction.
STRICT read-only slice — zero execution behaviour changes.

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Test Result

| Assembly | Passed | Failed | Skipped | Duration |
|---|---|---|---|---|
| OpsCopilot.Modules.AgentRuns.Tests | 53 | 0 | 0 | 300 ms |
| OpsCopilot.Modules.SafeActions.Tests | 245 | 0 | 0 | 1 s |
| OpsCopilot.Integration.Tests | 24 | 0 | 0 | 8 s |
| OpsCopilot.Mcp.ContractTests | 8 | 0 | 0 | 21 s |
| **Total** | **330** | **0** | **0** | — |

Baseline (Slice 15): 318 tests. New Slice 16 tests: **12**. Total: **330**.

## New Tests (SafeActionDetailAuditEndpointTests — 12 tests)

| # | Test Name | AC |
|---|---|---|
| 1 | GetById_ReturnsApprovalHistory | AC-1, AC-2 |
| 2 | GetById_ReturnsExecutionLogHistory | AC-3, AC-4 |
| 3 | GetById_ReturnsEmptyCollections_WhenNoAuditData | AC-5 |
| 4 | GetById_Returns404_WhenRecordNotFound | AC-6 |
| 5 | GetById_RedactsSensitiveKeys_InExecutionLogPayloads | AC-7, AC-8 |
| 6 | GetById_IncludesAuditSummaryFields | AC-9 |
| 7 | GetById_ReturnsMultipleApprovals_InChronologicalOrder | AC-10 |
| 8 | GetById_HandlesNullResponsePayload_InExecutionLog | AC-11 |
| 9 | PayloadRedactor_RedactsAllSensitiveKeys | AC-7 unit |
| 10 | PayloadRedactor_ReturnsNull_ForNullInput | AC-7 edge |
| 11 | PayloadRedactor_ReturnsOriginal_ForNonJson | AC-7 edge |
| 12 | PayloadRedactor_HandlesNestedObjects | AC-7 nested |

## Files Changed

### New Files
| File | Purpose |
|---|---|
| `src/Modules/SafeActions/Presentation/.../Contracts/ApprovalDetailResponse.cs` | DTO for approval detail |
| `src/Modules/SafeActions/Presentation/.../Contracts/ExecutionLogDetailResponse.cs` | DTO for execution log detail (with redaction) |
| `src/Modules/SafeActions/Presentation/.../Helpers/PayloadRedactor.cs` | Recursive JSON sensitive-key redactor |
| `tests/Modules/SafeActions/.../SafeActionDetailAuditEndpointTests.cs` | 12 HTTP-level tests |

### Modified Files
| File | Change |
|---|---|
| `src/Modules/SafeActions/Domain/.../IActionRecordRepository.cs` | +2 methods: `GetApprovalsForActionAsync`, `GetExecutionLogsForActionAsync` |
| `src/Modules/SafeActions/Infrastructure/.../SqlActionRecordRepository.cs` | EF implementations for both new methods |
| `src/Modules/SafeActions/Application/.../SafeActionOrchestrator.cs` | 2 pass-through query methods |
| `src/Modules/SafeActions/Presentation/.../Contracts/ActionRecordResponse.cs` | `Approvals` + `ExecutionLogs` collection props; new `From()` overload |
| `src/Modules/SafeActions/Presentation/.../Endpoints/SafeActionEndpoints.cs` | GET /{id} loads approvals/logs/audit, uses enriched From() |
| `docs/http/OpsCopilot.Api.http` | Section R added (6 requests, tenant-verify-16) |

## Acceptance Criteria Coverage

| AC | Description | Status |
|---|---|---|
| AC-1 | `GET /safe-actions/{id}` returns `approvals` array | ✅ |
| AC-2 | Each approval has identity, decision, reason, target, timestamp | ✅ |
| AC-3 | `GET /safe-actions/{id}` returns `executionLogs` array | ✅ |
| AC-4 | Each log has type, success, durationMs, payloads, timestamp | ✅ |
| AC-5 | Empty collections when no audit data | ✅ |
| AC-6 | 404 when record not found | ✅ |
| AC-7 | Sensitive keys redacted in execution log payloads | ✅ |
| AC-8 | Redaction covers request and response payloads | ✅ |
| AC-9 | Audit summary fields included | ✅ |
| AC-10 | Multiple approvals returned in order | ✅ |
| AC-11 | Null response payload handled gracefully | ✅ |
| AC-12 | Read-only — no execution behaviour changed | ✅ |
| AC-13 | .http Section R with tenant-verify-16 | ✅ |
| AC-14 | Evidence file created | ✅ |
| AC-15 | 0 warnings, 0 errors | ✅ |
| AC-16 | All prior tests still pass (318 baseline) | ✅ |
