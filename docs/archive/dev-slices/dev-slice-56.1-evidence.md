# Dev Slice 56.1 — Real Tenant Model Routing + Prompt Registry + Degraded Semantics

## Objective
Replace null-object stubs with real implementations:
- `IModelRoutingPolicy` backed by `ITenantConfigStore` (key `"model:triage"`)
- `IPromptVersionService` backed by `IConfiguration`
- LLM block executes before `CompleteRunAsync`; exceptions set `Degraded`
- Introduce `ModelCostEstimator` with per-model input/output rates
- Make both interfaces async (`Task<T>`)

---

## Constraints (Respected)
- No new HTTP routes
- No DB schema changes or migrations
- No breaking DTO changes (additive fields only)
- No secrets in logs
- All config keys verified in existing `*Options.cs` / appsettings files (or marked TODO)

---

## Files Changed

### New / New implementations
| File | Purpose |
|---|---|
| `src/Modules/AgentRuns/Application/.../Abstractions/IModelRoutingPolicy.cs` | Made async: `Task<ModelDescriptor> SelectModelAsync(...)` |
| `src/Modules/AgentRuns/Application/.../Abstractions/IPromptVersionService.cs` | Made async: `Task<PromptVersionInfo> GetCurrentVersionAsync(...)` |
| `src/Modules/AgentRuns/Application/.../Abstractions/NullModelRoutingPolicy.cs` | Updated for async signature |
| `src/Modules/AgentRuns/Application/.../Abstractions/NullPromptVersionService.cs` | Updated for async signature |
| `src/Modules/AgentRuns/Application/.../Orchestration/ModelCostEstimator.cs` | Static estimator — `"gpt-4o"` @ $2.50/M input, $10.00/M output |
| `src/Modules/AgentRuns/Infrastructure/.../Routing/TenantConfigModelRoutingPolicy.cs` | Real impl: reads `ITenantConfigStore` key `"model:triage"` |
| `src/Modules/AgentRuns/Infrastructure/.../Prompt/ConfigPromptVersionService.cs` | Real impl: reads `IConfiguration["PromptRegistry:Triage:Version"]` etc. |

### Modified
| File | Change |
|---|---|
| `src/Modules/AgentRuns/Application/.../Orchestration/TriageOrchestrator.cs` | LLM block now before `CompleteRunAsync`; exceptions set `Degraded`; async routing/prompt calls |
| `src/Modules/AgentRuns/Infrastructure/.../AgentRunsInfrastructureExtensions.cs` | Registers `TenantConfigModelRoutingPolicy` + `ConfigPromptVersionService` as real singletons |
| `src/Modules/AgentRuns/Application/.../AgentRunsApplicationExtensions.cs` | Null registrations removed (real impls registered in Infrastructure) |
| `src/Modules/AgentRuns/Infrastructure/.../OpsCopilot.AgentRuns.Infrastructure.csproj` | Added `InternalsVisibleTo` entries; fixed Tenancy project reference path |
| `tests/Modules/AgentRuns/.../OpsCopilot.Modules.AgentRuns.Tests.csproj` | Added Tenancy.Application ProjectReference |

### New Tests
| File | Tests |
|---|---|
| `tests/Modules/AgentRuns/.../TenantConfigModelRoutingPolicyTests.cs` | 3 tests: matched config → model id, no match → "default", invalid GUID → "default" (store not called) |
| `tests/Modules/AgentRuns/.../ConfigPromptVersionServiceTests.cs` | 2 tests: configured values returned, missing keys → fallback "0.0.0" / default prompt |
| `TriageOrchestratorTests.cs` (appended) | 3 new tests: LLM populates ledger fields; LLM throws → Degraded; exact cost pinned for gpt-4o 1M/1M tokens = $12.50 |

---

## Build Gate

```
dotnet build OpsCopilot.sln --no-incremental -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Test Gate

```
dotnet test --no-build

Passed!  - Failed: 0, Passed:  31  - OpsCopilot.Modules.Governance.Tests
Passed!  - Failed: 0, Passed:  30  - OpsCopilot.Modules.Connectors.Tests
Passed!  - Failed: 0, Passed:  15  - OpsCopilot.Modules.Evaluation.Tests
Passed!  - Failed: 0, Passed:  31  - OpsCopilot.Modules.AlertIngestion.Tests
Passed!  - Failed: 0, Passed:  27  - OpsCopilot.Modules.Reporting.Tests
Passed!  - Failed: 0, Passed:  94  - OpsCopilot.Modules.AgentRuns.Tests  ← +5 new
Passed!  - Failed: 0, Passed:  17  - OpsCopilot.Modules.Tenancy.Tests
Passed!  - Failed: 0, Passed: 303  - OpsCopilot.Modules.Packs.Tests
Passed!  - Failed: 0, Passed: 368  - OpsCopilot.Modules.SafeActions.Tests
Passed!  - Failed: 0, Passed:  24  - OpsCopilot.Integration.Tests
Passed!  - Failed: 0, Passed:   8  - OpsCopilot.Mcp.ContractTests

Total: 948 passed, 0 failed  (baseline: 911)
```

---

## Acceptance Criteria

| AC | Status |
|---|---|
| `IModelRoutingPolicy.SelectModelAsync` is async | ✅ |
| `IPromptVersionService.GetCurrentVersionAsync` is async | ✅ |
| `TenantConfigModelRoutingPolicy` reads `ITenantConfigStore` key `"model:triage"` | ✅ |
| `ConfigPromptVersionService` reads `IConfiguration` with fallbacks | ✅ |
| LLM exception sets `finalStatus = Degraded` (not `Completed`) | ✅ |
| `ModelCostEstimator.Estimate("gpt-4o", 1_000_000, 1_000_000)` == `12.50m` | ✅ |
| Real impls registered in DI; null stubs removed from Application DI | ✅ |
| Build: 0 errors, 0 warnings | ✅ |
| Tests: ≥911 passed, 0 failed | ✅ (948) |
