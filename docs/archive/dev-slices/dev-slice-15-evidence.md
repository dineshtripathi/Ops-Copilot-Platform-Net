# Dev Slice 15 — Evidence Report

## SafeActions: Execution Audit Querying (Read-Only)

---

## 1. Verdict

**PASS** — All 17 acceptance criteria (AC-1 through AC-17) met. Build clean, all 318 tests green.

---

## 2. Completion

**100 %**

---

## 3. Completed Items with Evidence

### A. Query Filters (AC-1 through AC-6)

| AC | Criterion | Status | Evidence |
|---|---|---|---|
| AC-1 | Filter by `actionType` (string) | ✅ | `SafeActionEndpoints.cs` L91 query param; `SqlActionRecordRepository.QueryByTenantAsync` `.Where(a => a.ActionType == actionType)` |
| AC-2 | Filter by `status` (ActionStatus enum) | ✅ | `SafeActionEndpoints.cs` L88 `Enum.TryParse<ActionStatus>` → query param; SQL repo `.Where(a => a.Status == status.Value)` |
| AC-3 | Filter by `rollbackStatus` (RollbackStatus enum) | ✅ | `SafeActionEndpoints.cs` L89 `Enum.TryParse<RollbackStatus>` → query param; SQL repo `.Where(a => a.RollbackStatus == rollbackStatus.Value)` |
| AC-4 | Filter by `hasExecutionLogs` (bool) | ✅ | SQL repo `.Where(a => a.ExecutionLogs.Any())` / `.Where(a => !a.ExecutionLogs.Any())` |
| AC-5 | Date range `fromUtc`/`toUtc` (exclusive upper bound) | ✅ | SQL repo `.Where(a => a.CreatedAtUtc >= fromUtc.Value)` / `.Where(a => a.CreatedAtUtc < toUtc.Value)` |
| AC-6 | All filters combinable | ✅ | Conditional LINQ `.Where()` chaining; test `List_CombinedFilters_CallsQueryByTenant` confirms |

### B. Tenant Isolation & Validation (AC-7, AC-8)

| AC | Criterion | Status | Evidence |
|---|---|---|---|
| AC-7 | `x-tenant-id` header required | ✅ | `SafeActionEndpoints.cs` L86 `tenantId` from header; missing → 400; test `List_Returns400_WhenTenantHeaderMissing` |
| AC-8 | Invalid enum/date values → 400 | ✅ | Enum.TryParse failure → `Results.BadRequest("Invalid status...")` ; DateTimeOffset.TryParse failure → `Results.BadRequest("Invalid fromUtc...")`; fromUtc > toUtc → `Results.BadRequest("fromUtc must be ≤ toUtc")` |

### C. Audit Summary Fields (AC-9)

| AC | Criterion | Status | Evidence |
|---|---|---|---|
| AC-9 | 6 audit summary fields in list response | ✅ | `AuditSummary.cs` record with 6 properties; `ActionRecordResponse.cs` includes all 6; `GetAuditSummariesAsync` enriches responses |

**Audit summary fields:** `ExecutionLogCount`, `LastExecutionAtUtc`, `LastExecutionSuccess`, `LastApprovalDecision`, `LastApprovalAtUtc`, `ApprovalCount`

### D. No Behavioral Changes (AC-10, AC-11)

| AC | Criterion | Status | Evidence |
|---|---|---|---|
| AC-10 | No route/MCP/Worker changes | ✅ | No files added in MCP/Worker or route registration; only existing `/safe-actions` list endpoint enhanced |
| AC-11 | No execution behavior changes | ✅ | Execute, approve, rollback orchestrator methods untouched; only new query pass-through methods added |

### E. Build & Test (AC-12, AC-13, AC-14)

| AC | Criterion | Status | Evidence |
|---|---|---|---|
| AC-12 | Build with 0 errors/warnings | ✅ | `dotnet build` — 42/42 projects, 0 errors, 0 warnings |
| AC-13 | All tests pass | ✅ | `dotnet test` — 318 total, 0 failed, 0 skipped |
| AC-14 | Conformance passes | ✅ | No cross-module coupling; layers properly separated |

### F. Documentation (AC-15, AC-16, AC-17)

| AC | Criterion | Status | Evidence |
|---|---|---|---|
| AC-15 | `.http` Section Q with `tenant-verify-15` | ✅ | `OpsCopilot.Api.http` Section Q, 14 requests (Q1-Q14) |
| AC-16 | `dev-slice-15-evidence.md` | ✅ | This file |
| AC-17 | `dev-slice-14-evidence.md` backfill | ✅ | `docs/dev-slice-14-evidence.md` created |

### G. `.http` File — Section Q (tenant ID: `tenant-verify-15`)

| # | Description | Expected |
|---|---|---|
| Q1 | Default query (no filters) | 200 |
| Q2 | Filter by `status=Completed` | 200 |
| Q3 | Filter by `rollbackStatus=Available` | 200 |
| Q4 | Filter by `actionType=ScaleOut` | 200 |
| Q5 | Filter by `hasExecutionLogs=true` | 200 |
| Q6 | Filter by date range | 200 |
| Q7 | Combined filters | 200 |
| Q8 | Custom limit=10 | 200 |
| Q9 | Invalid status → 400 | 400 |
| Q10 | Invalid rollbackStatus → 400 | 400 |
| Q11 | Invalid fromUtc → 400 | 400 |
| Q12 | fromUtc after toUtc → 400 | 400 |
| Q13 | Missing tenant header → 400 | 400 |
| Q14 | RunId-only path | 200 |

### H. Tests — 22 Slice 15 Tests (all passing)

**SafeActionOrchestratorTests (4 unit tests):**

| # | Test Name | AC |
|---|---|---|
| 1 | `QueryByTenantAsync_Forwards_All_Parameters_To_Repository` | AC-1–6 |
| 2 | `QueryByTenantAsync_Passes_Nulls_When_No_Filters` | AC-1–6 |
| 3 | `GetAuditSummariesAsync_Forwards_Ids_To_Repository` | AC-9 |
| 4 | `GetAuditSummariesAsync_Returns_Empty_Dict_For_Empty_Input` | AC-9 |

**SafeActionQueryEndpointTests (18 HTTP-level tests):**

| # | Test Name | AC |
|---|---|---|
| 5 | `List_Returns400_WhenTenantHeaderMissing` | AC-7 |
| 6 | `List_Returns400_WhenStatusInvalid` | AC-8 |
| 7 | `List_Returns400_WhenRollbackStatusInvalid` | AC-8 |
| 8 | `List_Returns400_WhenFromUtcInvalid` | AC-8 |
| 9 | `List_Returns400_WhenToUtcInvalid` | AC-8 |
| 10 | `List_Returns400_WhenFromUtcAfterToUtc` | AC-8 |
| 11 | `List_FilterByStatus_CallsQueryByTenant` | AC-2 |
| 12 | `List_FilterByRollbackStatus_CallsQueryByTenant` | AC-3 |
| 13 | `List_FilterByActionType_CallsQueryByTenant` | AC-1 |
| 14 | `List_FilterByHasExecutionLogs_CallsQueryByTenant` | AC-4 |
| 15 | `List_FilterByDateRange_CallsQueryByTenant` | AC-5 |
| 16 | `List_CombinedFilters_CallsQueryByTenant` | AC-6 |
| 17 | `List_Response_ContainsAuditSummaryFields` | AC-9 |
| 18 | `List_Response_ContainsEmptyAuditWhenNoData` | AC-9 |
| 19 | `List_RunIdOnly_CallsListByRunAsync` | routing |
| 20 | `List_NoParams_CallsQueryByTenant` | routing |
| 21 | `List_StatusParsing_IsCaseInsensitive` | AC-8 |
| 22 | `List_LimitClamped_To_MaxListLimit` | AC-6 |

---

## 4. Missing Items

None.

---

## 5. Deviations from Acceptance Criteria

None. All hard constraints preserved:
- No schema changes
- No new routes — existing list endpoint enhanced
- No MCP/Worker changes
- No new Azure SDKs
- No execution behavior changes (execute, approve, rollback untouched)
- Slice 14 replay guard behavior preserved
- 501 guard precedence preserved
- Tenant execution policy behavior preserved

---

## 6. Commands Executed

```bash
dotnet build OpsCopilot.sln --verbosity minimal
dotnet test OpsCopilot.sln --verbosity minimal
```

---

## 7. Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

42/42 projects compiled. Duration: 15.8s.

---

## 8. Test Result

```
Test summary: total: 318, failed: 0, succeeded: 318, skipped: 0, duration: 48.0s
```

| Project | Count |
|---|---|
| SafeActions.Tests | ~229 |
| AgentRuns.Tests | 53 |
| Integration.Tests | 24 |
| McpContractTests | 8 |
| Other | 4 |
| **Total** | **318** |

Test delta: 296 (Slice 14) → 318 (Slice 15) = **+22 new tests**

---

## 9. Conformance

| Rule | Status |
|---|---|
| Domain record (`AuditSummary`) in Domain layer | ✅ |
| Repository interface in Domain layer | ✅ |
| Repository implementation in Infrastructure layer | ✅ |
| Orchestrator pass-through in Application layer | ✅ |
| Endpoint filter logic in Presentation layer | ✅ |
| No cross-module coupling | ✅ |
| Existing tests unbroken | ✅ All 296 pre-Slice-15 tests still pass |

---

## 10. Commit Readiness

### New Files (2)
- `src/Modules/SafeActions/Domain/OpsCopilot.SafeActions.Domain/AuditSummary.cs`
- `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/SafeActionQueryEndpointTests.cs`

### Modified Files (7)
- `src/Modules/SafeActions/Domain/OpsCopilot.SafeActions.Domain/Repositories/IActionRecordRepository.cs`
- `src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/Persistence/SqlActionRecordRepository.cs`
- `src/Modules/SafeActions/Presentation/OpsCopilot.SafeActions.Presentation/Contracts/ActionRecordResponse.cs`
- `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/SafeActionOrchestrator.cs`
- `src/Modules/SafeActions/Presentation/OpsCopilot.SafeActions.Presentation/Endpoints/SafeActionEndpoints.cs`
- `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/SafeActionOrchestratorTests.cs`
- `docs/http/OpsCopilot.Api.http`

### Evidence Files (2)
- `docs/dev-slice-14-evidence.md` (backfill)
- `docs/dev-slice-15-evidence.md` (this file)

**Total: 11 files** — Ready for commit.
