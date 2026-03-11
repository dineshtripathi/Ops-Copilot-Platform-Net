# Dev Slice 32 — SafeActions Tenant-Aware Governance Wiring

## Objective

Wire the tenant-aware governance pipeline through the SafeActions module so that
`GovernancePolicyClient` (the bridge between SafeActions and Governance) resolves
per-tenant tool-allowlist and token-budget rules via the 3-tier
`TenantAwareGovernanceOptionsResolver` (SQL → config override → defaults).

## Deliverables

| ID | Description | Status |
|----|-------------|--------|
| A  | DI order fix — Tenancy before Governance in `Program.cs` | ✅ Done |
| B  | `Guid? correlationId` parameter added to `IGovernancePolicyClient.EvaluateTokenBudget` + all call sites + 29 test mock sites | ✅ Done |
| C  | ≥ 10 integration tests in `SafeActionsTenantAwareGovernanceIntegrationTests.cs` | ✅ Done (12 tests) |
| D  | `.http` Section AI documentation (5 test cases) | ✅ Done |
| E  | This evidence document | ✅ Done |

## Files Modified

### Source Files

| File | Change |
|------|--------|
| `src/Hosts/OpsCopilot.ApiHost/Program.cs` | Moved `.AddTenancyModule()` before `.AddGovernanceModule()` so `ITenantConfigProvider` is available at resolve time |
| `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Abstractions/IGovernancePolicyClient.cs` | Added `Guid? correlationId = null` parameter with xmldoc |
| `src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/Policies/GovernancePolicyClient.cs` | Uses `correlationId ?? DeterministicGuid(tenantId, actionType)` as runId |
| `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/SafeActionOrchestrator.cs` | Both `ExecuteAsync` and `ExecuteRollbackAsync` now pass `actionRecordId` as `correlationId` |

### Test Files (29 mock sites updated across 10 files)

| File | Mock Sites |
|------|------------|
| `SafeActionOrchestratorTests.cs` | 14 |
| `SafeActionIdentityEndpointTests.cs` | 2 |
| `SafeActionTenantExecutionPolicyEndpointTests.cs` | 3 |
| `SafeActionExecutionThrottleEndpointTests.cs` | 2 |
| `SafeActionsTelemetryTests.cs` | 1 |
| `SafeActionExecutionGuardTests.cs` | 1 |
| `SafeActionRoutingEndpointTests.cs` | 1 |
| `SafeActionQueryEndpointTests.cs` | 1 |
| `SafeActionDetailAuditEndpointTests.cs` | 1 |
| `SafeActionDryRunEndpointTests.cs` | 3 |

All 29 mock setups updated from 4-parameter `It.IsAny` to 5-parameter
(adding `It.IsAny<Guid?>()` for the new `correlationId` parameter).

### New Files

| File | Purpose |
|------|---------|
| `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/SafeActionsTenantAwareGovernanceIntegrationTests.cs` | 12 integration tests covering the full cross-module chain |
| `docs/http/OpsCopilot.Api.http` (Section AI appended) | 5 manual test cases for tenant-aware governance wiring |
| `docs/dev-slice-32-evidence.md` | This file |

### Project File Updates

| File | Change |
|------|--------|
| `tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/OpsCopilot.Modules.SafeActions.Tests.csproj` | Added 3 cross-module `ProjectReference` entries: `OpsCopilot.Governance.Application`, `OpsCopilot.Tenancy.Application`, `OpsCopilot.Tenancy.Infrastructure` |

## Resolution Chain Under Test

```
SafeActionOrchestrator
  → IGovernancePolicyClient (bridge — GovernancePolicyClient)
    → IToolAllowlistPolicy (DefaultToolAllowlistPolicy)
    → ITokenBudgetPolicy (DefaultTokenBudgetPolicy)
      → ITenantAwareGovernanceOptionsResolver (TenantAwareGovernanceOptionsResolver)
        → ITenantConfigProvider (TenantConfigProviderAdapter)
          → ITenantConfigResolver (SQL / mocked in tests)
```

### 3-Tier Resolution Priority

1. **SQL** — `ITenantConfigResolver.ResolveAsync(tenantGuid)` returns `EffectiveTenantConfig`
2. **Config Override** — `GovernanceOptions.TenantOverrides[tenantId]` from `appsettings.json`
3. **Defaults** — `GovernanceOptions.Defaults` (AllowedTools: `kql_query`, `runbook_search`; TokenBudget: null)

If SQL throws, the resolver catches the exception and falls to tier 2/3 gracefully.

## Integration Test Coverage (12 tests)

| # | Test | Scenario |
|---|------|----------|
| 1 | `Bridge_RestrictedTenant_DeniesUnlistedTool` | Restricted SQL allowlist → tool denied with `TOOL_DENIED` |
| 2 | `Bridge_PermissiveTenant_AllowsListedTool` | Tool in SQL allowlist → allowed with `ALLOWED` |
| 3 | `Bridge_CappedBudget_ReturnsMaxTokens` | SQL budget=2048 → `MaxTokens=2048` |
| 4 | `Bridge_NullBudget_ReturnsUnlimited` | SQL budget=null → `MaxTokens=null` (unlimited) |
| 5 | `Bridge_ExplicitCorrelationId_PassedAsBudgetRunId` | Explicit GUID propagated through bridge to budget policy |
| 6 | `Bridge_NullCorrelationId_DeterministicGuidIsStable` | Null correlationId → stable deterministic GUID fallback |
| 7 | `Bridge_TwoTenants_IsolatedGovernance` | Two tenants with different allowlists → isolated decisions |
| 8 | `Bridge_SqlUnavailable_FallsBackToConfigOverride` | SQL throws → config-file `TenantOverrides` win |
| 9 | `Bridge_SqlUnavailableNoOverride_FallsToDefaults` | SQL throws, no override → `GovernanceOptions.Defaults` win |
| 10 | `Bridge_EmptyAllowlist_AllToolsAllowed` | Empty SQL allowlist → open gate (all tools allowed) |
| 11 | `Bridge_InvalidTenantId_FallsToDefaults` | Non-GUID tenantId → adapter returns null → defaults |
| 12 | `Bridge_SqlOverridesConfigFile_SqlWins` | SQL + config-file both present → SQL tier wins |

## .http Section AI Test Cases

| # | Test | Purpose |
|---|------|---------|
| AI1 | Restricted-tenant propose denied | Tool not in SQL allowlist → 400 `governance_tool_denied` |
| AI2 | Restricted-tenant propose allowed | Tool in SQL allowlist → 200 proposed |
| AI3 | Different tenant isolation | Tenant B different allowlist → 400 on Tenant A tool |
| AI4 | Budget-capped execute | MaxTokens honoured on execute path |
| AI5 | CorrelationId propagation | Explicit GUID forwarded to budget check |

## DI Registration Order (Final)

```csharp
builder.Services
    .AddAgentRunsModule(builder.Configuration)
    .AddAlertIngestionModule()
    .AddTenancyModule(builder.Configuration)       // ← must precede Governance
    .AddGovernanceModule(builder.Configuration, startupLogger)
    .AddSafeActionsModule(builder.Configuration)
    .AddReportingModule(builder.Configuration)
    .AddEvaluationModule()
    .AddConnectorsModule();
```

## Reason Codes (Frozen)

| Code | Source |
|------|--------|
| `governance_tool_denied` | Tool not in tenant's allowed-tools list |
| `governance_budget_exceeded` | Token budget exceeded or capped |

## Baseline

- **Prior commits**: `1f67002`, `1efcd1a`, `a6873c3` (all Slice 31.1)
- **Pre-Slice-32 test count**: 593 passing
- **New tests added in Slice 32**: 12 (SafeActionsTenantAwareGovernanceIntegrationTests)

### Verified Test Results (`dotnet test OpsCopilot.sln`)

| Assembly | Passed | Failed | Skipped | Total |
|----------|-------:|-------:|--------:|------:|
| OpsCopilot.Modules.SafeActions.Tests | 353 | 0 | 0 | 353 |
| OpsCopilot.Modules.AgentRuns.Tests | 69 | 0 | 0 | 69 |
| OpsCopilot.Modules.AlertIngestion.Tests | 31 | 0 | 0 | 31 |
| OpsCopilot.Modules.Governance.Tests | 31 | 0 | 0 | 31 |
| OpsCopilot.Modules.Connectors.Tests | 30 | 0 | 0 | 30 |
| OpsCopilot.Modules.Reporting.Tests | 27 | 0 | 0 | 27 |
| OpsCopilot.Integration.Tests | 24 | 0 | 0 | 24 |
| OpsCopilot.Modules.Tenancy.Tests | 17 | 0 | 0 | 17 |
| OpsCopilot.Modules.Evaluation.Tests | 15 | 0 | 0 | 15 |
| OpsCopilot.Mcp.ContractTests | 8 | 0 | 0 | 8 |
| **Grand Total** | **605** | **0** | **0** | **605** |

**Result**: 593 + 12 = **605 passing, 0 failed, 0 skipped** ✅
