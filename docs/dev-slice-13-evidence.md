# Dev Slice 13 — Evidence Report

## SafeActions: Tenant-Scoped Execution Policy

---

## 1. Verdict

**PASS** — All acceptance criteria met. Build clean, all 286 tests green.

---

## 2. Completion

**100 %**

---

## 3. Completed Items with Evidence

### A. Interface — `ITenantExecutionPolicy`

| Criterion | Status | Evidence |
|---|---|---|
| Interface declared | ✅ | `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Abstractions/ITenantExecutionPolicy.cs` (22 lines) |
| Method signature | ✅ | `PolicyDecision EvaluateExecution(string tenantId, string actionType)` |
| Returns `PolicyDecision` from BuildingBlocks.Contracts.Governance | ✅ | Uses `PolicyDecision.Allow()` / `PolicyDecision.Deny(reasonCode, message)` |

### B. Implementation — `ConfigDrivenTenantExecutionPolicy`

| Criterion | Status | Evidence |
|---|---|---|
| Sealed class | ✅ | `src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/Policies/ConfigDrivenTenantExecutionPolicy.cs` (~80 lines) |
| Config path `SafeActions:AllowedExecutionTenants:{actionType}` | ✅ | Reads from `IConfiguration` using `GetSection().GetChildren()` |
| Deny-by-default | ✅ | Missing key / empty array / tenant not listed → `PolicyDecision.Deny(...)` |
| Allow only when tenant is in allowlist | ✅ | Case-insensitive comparison via `OrdinalIgnoreCase` |
| Diagnostic properties | ✅ | `ConfiguredActionTypeCount`, `TotalTenantEntryCount` (internal) |
| Nullability clean | ✅ | `.Select(v => v!)` after null-filtering (CS8619 fix applied) |

### C. Orchestrator Integration

| Criterion | Status | Evidence |
|---|---|---|
| `SafeActionOrchestrator` accepts `ITenantExecutionPolicy` | ✅ | 5th constructor param, `SafeActionOrchestrator.cs` |
| `ExecuteAsync` gates on tenant policy | ✅ | Line 135-147: `EvaluateExecution()` → `PolicyDeniedException` if denied |
| `ExecuteRollbackAsync` gates on tenant policy | ✅ | Same pattern in rollback path |
| Throws `PolicyDeniedException` with reason code | ✅ | `throw new PolicyDeniedException(tenantDecision)` |

### D. Endpoint Integration

| Criterion | Status | Evidence |
|---|---|---|
| Execute endpoint catches `PolicyDeniedException` → 400 | ✅ | `SafeActionEndpoints.cs`, returns `{ reasonCode, message }` |
| Rollback-execute endpoint catches `PolicyDeniedException` → 400 | ✅ | Same pattern |
| 501 guard fires BEFORE tenant policy | ✅ | `EnableExecution=false` → 501 immediately, orchestrator never called |

### E. DI Registration & Startup Diagnostics

| Criterion | Status | Evidence |
|---|---|---|
| Singleton registration | ✅ | `SafeActionsInfrastructureExtensions.cs` — `AddSingleton<ITenantExecutionPolicy, ConfigDrivenTenantExecutionPolicy>()` |
| Startup log | ✅ | Logs `ConfiguredActionTypeCount` and `TotalTenantEntryCount` at `Information` level |

### F. Configuration

| Criterion | Status | Evidence |
|---|---|---|
| `appsettings.Development.json` | ✅ | `"AllowedExecutionTenants": {}` in SafeActions section |

### G. `.http` File — Section O (tenant ID: `tenant-verify-13`)

| Criterion | Status | Evidence |
|---|---|---|
| O1 — Execute authorized tenant (`tenant-verify-13`) | ✅ | `OpsCopilot.Api.http` lines 622-628, expected 200 |
| O2 — Execute unauthorized tenant (`tenant-blocked`) | ✅ | Lines 630-636, expected 400 |
| O3 — Execute missing action-type config (`tenant-verify-13`) | ✅ | Lines 638-644, expected 400 |
| O4 — Rollback-execute unauthorized tenant (`tenant-blocked`) | ✅ | Lines 646-653, expected 400 |
| O5 — 501 precedence wins over tenant deny (`tenant-blocked`) | ✅ | Lines 655-663, expected 501 |

### H. Tests — 18 Slice 13 Tests (all passing)

**TenantExecutionPolicyTests (11 unit tests):**

| # | Test Name | Status |
|---|---|---|
| 1 | `Empty_config_denies_any_action` | ✅ |
| 2 | `Missing_action_type_key_denies` | ✅ |
| 3 | `Empty_tenant_array_denies` | ✅ |
| 4 | `Tenant_not_in_allowlist_denies` | ✅ |
| 5 | `Tenant_in_allowlist_allows` | ✅ |
| 6 | `Multiple_tenants_in_allowlist_allows` | ✅ |
| 7 | `Action_type_lookup_is_case_insensitive` | ✅ |
| 8 | `Tenant_id_lookup_is_case_insensitive` | ✅ |
| 9 | `Different_action_types_have_independent_allowlists` | ✅ |
| 10 | `Diagnostic_properties_reflect_config` | ✅ |
| 11 | `Diagnostic_properties_zero_when_empty` | ✅ |

**SafeActionTenantExecutionPolicyEndpointTests (4 endpoint tests):**

| # | Test Name | Status |
|---|---|---|
| 12 | `Execute_Returns400_WithReasonCode_WhenTenantDenied` | ✅ |
| 13 | `RollbackExecute_Returns400_WithReasonCode_WhenTenantDenied` | ✅ |
| 14 | `Execute_Returns200_WhenTenantAllowed` | ✅ |
| 15 | `Execute_Returns501_EvenWhenTenantDenied_IfExecutionDisabled` | ✅ |

**SafeActionOrchestratorTests (3 orchestrator tests):**

| # | Test Name | Status |
|---|---|---|
| 16 | `ProposeAsync_Throws_PolicyDeniedException_When_Policy_Denies` | ✅ |
| 17 | `ExecuteAsync_Throws_PolicyDeniedException_When_Tenant_Not_Authorized` | ✅ |
| 18 | `ExecuteRollbackAsync_Throws_PolicyDeniedException_When_Tenant_Not_Authorized` | ✅ |

---

## 4. Missing Items

None.

---

## 5. Deviations from Acceptance Criteria

None. All hard constraints preserved:
- No schema changes
- No route changes
- No MCP/Worker changes
- No new Azure SDKs
- No changes to Slice 12 behavior
- 501 guard precedence preserved
- Deterministic executor error behavior preserved
- No RBAC/auth redesigns

---

## 6. Commands Executed (Recovery Session)

```
dotnet build OpsCopilot.sln --verbosity minimal          # Initial build → 3 errors + 1 warning
# Applied 4 compile fixes (ActionRecord.Create, ActionExecutionResult 3-param, RollbackStatus.Approved, nullability)
dotnet build OpsCopilot.sln --verbosity minimal          # Rebuild → 0 errors, 0 warnings
dotnet test OpsCopilot.sln --no-build --verbosity normal # Tests → failures (NullRef + missing mock)
# Applied runtime test fixes (PolicyDecision.Allow() mocks in 3 files + AppendExecutionLogAsync setup)
dotnet build OpsCopilot.sln --verbosity minimal          # Rebuild → 0 errors, 0 warnings
dotnet test OpsCopilot.sln --no-build --verbosity normal # Tests → 286 passed, 0 failed
```

---

## 7. Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:07.39
```

42/42 projects compiled.

---

## 8. Test Result

```
Test Run Successful.  Total tests: 53   (McpContractTests)
Test Run Successful.  Total tests: 201  (SafeActions.Tests)
Test Run Successful.  Total tests: 24   (AgentRuns.Tests)
Test Run Successful.  Total tests: 8    (Integration.Tests)
─────────────────────────────────────────
Grand Total:          286 tests — ALL PASSING
```

---

## 9. Conformance

| Rule | Status |
|---|---|
| Clean Architecture layers | ✅ Interface in Application, impl in Infrastructure |
| Dependency direction | ✅ Infrastructure → Application → Domain |
| No cross-module coupling | ✅ Uses BuildingBlocks.Contracts.Governance shared type |
| DI via extension method | ✅ Registered in `SafeActionsInfrastructureExtensions` |
| Config-driven, deny-by-default | ✅ |
| Existing tests unbroken | ✅ All 268 pre-Slice-13 tests still pass |

---

## 10. Commit Readiness

**READY TO COMMIT.** All code compiles cleanly, all 286 tests pass, all acceptance criteria met.

### Slice 13 Files (new + modified):

**New files (untracked):**
- `src/Modules/SafeActions/Application/.../Abstractions/ITenantExecutionPolicy.cs`
- `src/Modules/SafeActions/Infrastructure/.../Policies/ConfigDrivenTenantExecutionPolicy.cs`
- `tests/Modules/SafeActions/.../SafeActionTenantExecutionPolicyEndpointTests.cs`
- `tests/Modules/SafeActions/.../TenantExecutionPolicyTests.cs`

**Modified files:**
- `src/Modules/SafeActions/Application/.../Orchestration/SafeActionOrchestrator.cs` — tenant policy gate added
- `src/Modules/SafeActions/Presentation/.../Endpoints/SafeActionEndpoints.cs` — PolicyDeniedException catch
- `src/Modules/SafeActions/Infrastructure/.../Extensions/SafeActionsInfrastructureExtensions.cs` — DI + diagnostics
- `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` — AllowedExecutionTenants config
- `docs/http/OpsCopilot.Api.http` — Section O (O1-O5, tenant-verify-13)
- `tests/.../SafeActionOrchestratorTests.cs` — 3 new policy deny tests
- `tests/.../SafeActionDryRunEndpointTests.cs` — mock fix for ITenantExecutionPolicy
- `tests/.../SafeActionExecutionGuardTests.cs` — mock fix for ITenantExecutionPolicy
- `tests/.../SafeActionRoutingEndpointTests.cs` — mock fix for ITenantExecutionPolicy (was new in Slice 12, now modified)
