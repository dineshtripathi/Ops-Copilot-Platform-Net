# Slice 21 — SafeActions ActionType Catalog + Allowlist + Risk Tiering

**Status**: COMPLETE  
**Baseline commit**: `dcb2c0f` (slice 20)  
**Baseline tests**: 392 passing  
**Post-slice tests**: 405 passing (392 + 13 new)  

---

## Acceptance Criteria Checklist

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | `ActionRiskTier` enum with `Low`, `Medium`, `High` in Application layer | ✅ |
| AC-2 | `ActionTypeDefinition` record with `ActionType`, `RiskTier`, `Enabled` in Application layer | ✅ |
| AC-3 | `IActionTypeCatalog` interface with `IsAllowlisted`, `Get`, `List` in Application layer | ✅ |
| AC-4 | `ConfigActionTypeCatalog` reads `SafeActions:ActionTypes` config section | ✅ |
| AC-5 | Empty config → allow-all (backward-compatible) | ✅ |
| AC-6 | Populated config → unknown/disabled types denied at proposal time with `action_type_not_allowed` | ✅ |
| AC-7 | Catalog check fires BEFORE existing policy gate in `ProposeAsync` | ✅ |
| AC-8 | `ActionRecordResponse` includes optional `riskTier` field (nullable, runtime-derived) | ✅ |
| AC-9 | POST propose, GET detail, GET list all return `riskTier` when catalog available | ✅ |
| AC-10 | No new routes, no schema changes, no new NuGet packages | ✅ |
| AC-11 | No execution routing changes | ✅ |
| AC-12 | DI wiring as singleton with startup diagnostics log | ✅ |
| AC-13 | ≥10 new tests written and passing | ✅ (13 new) |
| AC-14 | All existing tests still pass (no regressions) | ✅ |
| AC-15 | .http documentation updated with Section W (`tenant-verify-21`) | ✅ |
| AC-16 | Evidence document created | ✅ (this file) |

---

## Files Created

| File | Description |
|------|-------------|
| `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Abstractions/ActionRiskTier.cs` | `enum ActionRiskTier { Low, Medium, High }` |
| `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Abstractions/ActionTypeDefinition.cs` | `sealed record ActionTypeDefinition(string ActionType, ActionRiskTier RiskTier, bool Enabled)` |
| `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Abstractions/IActionTypeCatalog.cs` | Interface: `IsAllowlisted`, `Get`, `List` |
| `src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/Policies/ConfigActionTypeCatalog.cs` | Config-driven catalog; allow-all when empty, deny unknown/disabled when populated |
| `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/ConfigActionTypeCatalogTests.cs` | 11 new unit tests for catalog behavior |

## Files Modified

| File | Change Summary |
|------|---------------|
| `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/SafeActionOrchestrator.cs` | Added `IActionTypeCatalog` as 5th constructor param; catalog allowlist check before policy gate in `ProposeAsync` |
| `src/Modules/SafeActions/Presentation/OpsCopilot.SafeActions.Presentation/Responses/ActionRecordResponse.cs` | Added `string? RiskTier` property; all `From()` overloads accept optional `riskTier` param |
| `src/Modules/SafeActions/Presentation/OpsCopilot.SafeActions.Presentation/Endpoints/SafeActionEndpoints.cs` | POST propose, GET detail, GET list resolve catalog and pass `riskTier` to response |
| `src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/SafeActionsInfrastructureExtensions.cs` | Registered `ConfigActionTypeCatalog` as singleton + startup diagnostics |
| `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` | Added `SafeActions:ActionTypes` with 5 entries |
| `tests/.../SafeActionOrchestratorTests.cs` | Updated `CreateOrchestrator` helper for catalog param; added 2 catalog tests |
| `tests/.../SafeActionsTelemetryTests.cs` | Updated `CreateOrchestrator` helper for catalog param |
| `tests/.../SafeActionQueryEndpointTests.cs` | Added `IActionTypeCatalog` DI registration |
| `tests/.../SafeActionDetailAuditEndpointTests.cs` | Added `IActionTypeCatalog` DI registration |
| `tests/.../SafeActionRoutingEndpointTests.cs` | Added `IActionTypeCatalog` DI registration |
| `tests/.../SafeActionExecutionGuardTests.cs` | Added `IActionTypeCatalog` DI registration |
| `tests/.../SafeActionDryRunEndpointTests.cs` | Added `IActionTypeCatalog` DI registration |
| `tests/.../SafeActionIdentityEndpointTests.cs` | Added `IActionTypeCatalog` DI registration (2 locations) |
| `tests/.../SafeActionExecutionThrottleEndpointTests.cs` | Added `IActionTypeCatalog` DI registration (2 locations) |
| `tests/.../SafeActionTenantExecutionPolicyEndpointTests.cs` | Added `IActionTypeCatalog` DI registration (3 locations) |
| `docs/http/OpsCopilot.Api.http` | Added Section W (catalog + allowlist verification) with tenant `tenant-verify-21` |

---

## New Tests (13)

| # | Test Name | Verifies |
|---|-----------|----------|
| 1 | `IsAllowlisted_EmptyConfig_ReturnsTrue_AllowAll` | Empty config = allow-all (backward-compat) |
| 2 | `IsAllowlisted_PopulatedConfig_KnownEnabledType_ReturnsTrue` | Known + enabled → allowed |
| 3 | `IsAllowlisted_PopulatedConfig_UnknownType_ReturnsFalse` | Unknown type → denied |
| 4 | `IsAllowlisted_PopulatedConfig_DisabledType_ReturnsFalse` | Disabled type → denied |
| 5 | `IsAllowlisted_CaseInsensitive_ReturnsTrue` | Case-insensitive lookup |
| 6 | `Get_KnownType_ReturnsCorrectDefinition` | `Get` returns correct definition |
| 7 | `Get_UnknownType_ReturnsNull` | `Get` returns null for unknown |
| 8 | `Get_EmptyConfig_ReturnsNull` | `Get` returns null when config empty |
| 9 | `List_ReturnsAllDefinitions` | `List` returns all entries |
| 10 | `Diagnostics_DefinitionCount_And_EnabledCount` | Internal counts correct |
| 11 | `Get_DefaultsToLow_WhenRiskTierMissing` | Missing RiskTier defaults to `Low` |
| 12 | `ProposeAsync_CatalogDenies_ThrowsPolicyDenied_ActionTypeNotAllowed` | Orchestrator throws on catalog deny |
| 13 | `ProposeAsync_CatalogAllows_ProceedsToNextPolicy` | Orchestrator proceeds when catalog allows |

---

## Config Sample (`appsettings.Development.json`)

```json
"SafeActions": {
  "ActionTypes": [
    { "ActionType": "restart_pod",         "RiskTier": "High",   "Enabled": true },
    { "ActionType": "http_probe",          "RiskTier": "Low",    "Enabled": true },
    { "ActionType": "dry_run",             "RiskTier": "Low",    "Enabled": true },
    { "ActionType": "azure_resource_get",  "RiskTier": "Medium", "Enabled": true },
    { "ActionType": "azure_monitor_query", "RiskTier": "Medium", "Enabled": true }
  ]
}
```

---

## Deny Response (400)

```json
{
  "reasonCode": "action_type_not_allowed",
  "message": "Action type 'delete_universe' is not enabled in the catalog."
}
```

---

## Test Results

```
Passed!  - Failed: 0, Passed:  53, Total:  53 - AgentRuns
Passed!  - Failed: 0, Passed: 320, Total: 320 - SafeActions
Passed!  - Failed: 0, Passed:  24, Total:  24 - Integration
Passed!  - Failed: 0, Passed:   8, Total:   8 - MCP Contract
Total: 405 passing, 0 failed
```
