# Slice 25 — Evaluation MVP (STRICT, Deterministic, No Persistence)

## Evidence Document

| Item | Detail |
| --- | --- |
| **Slice** | 25 |
| **Title** | Evaluation MVP (STRICT, Deterministic, No Persistence) |
| **Branch** | main |
| **Baseline commit** | `bb55daf` (Slice 24) |
| **Baseline tests** | 466 |
| **Final tests** | 481 (466 + 15 new) |
| **Status** | All acceptance criteria met |

---

## Acceptance Criteria

| AC | Description | Status | Evidence |
| --- | --- | --- | --- |
| AC-1 | Domain records: `EvaluationScenario`, `EvaluationResult`, `EvaluationRunSummary` — sealed records, no persistence | ✅ | 3 sealed records in `Domain/Models/` with no EF or DB references |
| AC-2 | `IEvaluationScenario` abstraction with `ScenarioId`, `Module`, `Name`, `Category`, `Description`, `Execute()` | ✅ | `Abstractions/IEvaluationScenario.cs` — interface, 6 members |
| AC-3 | ≥10 deterministic evaluation scenarios across AlertIngestion, SafeActions, Reporting | ✅ | 11 scenarios: 4 AlertIngestion, 4 SafeActions, 3 Reporting |
| AC-4 | ≥3 AlertIngestion scenarios | ✅ | AI-001 AzureMonitorParse, AI-002 DatadogProvider, AI-003 EmptyPayloadRejection, AI-004 FingerprintDeterminism |
| AC-5 | ≥3 SafeActions scenarios | ✅ | SA-001 ActionTypeClassification, SA-002 EmptyActionList, SA-003 DryRunGuard, SA-004 ReplayDetection |
| AC-6 | ≥2 Reporting scenarios | ✅ | RP-001 SummaryTotals, RP-002 RecentLimitClamp, RP-003 TenantFilter |
| AC-7 | `EvaluationScenarioCatalog` holds all scenarios, exposes `GetMetadata()` projection | ✅ | `Services/EvaluationScenarioCatalog.cs` — ctor DI, `Scenarios` property, `GetMetadata()` |
| AC-8 | `EvaluationRunner` executes all scenarios deterministically, returns `EvaluationRunSummary` | ✅ | `Services/EvaluationRunner.cs` — `Run()` iterates catalog, builds summary with RunId/RanAtUtc/counts/results |
| AC-9 | `GET /evaluation/run` returns 200 with `EvaluationRunSummary` JSON | ✅ | `EvaluationEndpoints.cs` — MapGet `/run`, injects `EvaluationRunner`, returns `Results.Ok(summary)` |
| AC-10 | `GET /evaluation/scenarios` returns 200 with list of `EvaluationScenario` metadata | ✅ | `EvaluationEndpoints.cs` — MapGet `/scenarios`, injects `EvaluationScenarioCatalog` |
| AC-11 | Module wired via `AddEvaluationModule()` + `MapEvaluationEndpoints()` | ✅ | `Program.cs` — `.AddEvaluationModule()` in service chain, `app.MapEvaluationEndpoints()` in pipeline |
| AC-12 | ≥12 new tests, all passing | ✅ | 15 test methods in `EvaluationTests.cs`, all passing |
| AC-13 | .http Section AA with evaluation requests | ✅ | `docs/http/OpsCopilot.Api.http` — Section AA: AA1 (run), AA2 (scenarios) |

---

## New / Modified Files

### Domain (3 new files + 1 csproj)
- `src/Modules/Evaluation/Domain/OpsCopilot.Evaluation.Domain/OpsCopilot.Evaluation.Domain.csproj`
- `src/Modules/Evaluation/Domain/OpsCopilot.Evaluation.Domain/Models/EvaluationScenario.cs`
- `src/Modules/Evaluation/Domain/OpsCopilot.Evaluation.Domain/Models/EvaluationResult.cs`
- `src/Modules/Evaluation/Domain/OpsCopilot.Evaluation.Domain/Models/EvaluationRunSummary.cs`

### Application (15 new files + 1 csproj modified)
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/OpsCopilot.Evaluation.Application.csproj` (added DI.Abstractions 9.0.2)
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Abstractions/IEvaluationScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/AlertIngestion_AzureMonitorParseScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/AlertIngestion_DatadogProviderScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/AlertIngestion_EmptyPayloadRejectionScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/AlertIngestion_FingerprintDeterminismScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/SafeActions_ActionTypeClassificationScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/SafeActions_EmptyActionListScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/SafeActions_DryRunGuardScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/SafeActions_ReplayDetectionScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/Reporting_SummaryTotalsScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/Reporting_RecentLimitClampScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Scenarios/Reporting_TenantFilterScenario.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Services/EvaluationScenarioCatalog.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Services/EvaluationRunner.cs`
- `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/Extensions/EvaluationApplicationExtensions.cs`

### Presentation (2 new files + 1 csproj)
- `src/Modules/Evaluation/Presentation/OpsCopilot.Evaluation.Presentation/OpsCopilot.Evaluation.Presentation.csproj`
- `src/Modules/Evaluation/Presentation/OpsCopilot.Evaluation.Presentation/Extensions/EvaluationPresentationExtensions.cs`
- `src/Modules/Evaluation/Presentation/OpsCopilot.Evaluation.Presentation/Endpoints/EvaluationEndpoints.cs`

### Host (2 modified)
- `src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj` (added Evaluation Presentation reference)
- `src/Hosts/OpsCopilot.ApiHost/Program.cs` (added `AddEvaluationModule()` + `MapEvaluationEndpoints()`)

### Tests (2 new)
- `tests/Modules/Evaluation/OpsCopilot.Modules.Evaluation.Tests/OpsCopilot.Modules.Evaluation.Tests.csproj`
- `tests/Modules/Evaluation/OpsCopilot.Modules.Evaluation.Tests/EvaluationTests.cs` (15 test methods)

### .http (1 modified)
- `docs/http/OpsCopilot.Api.http` (added Section AA: AA1–AA2 + header index entry)

### Deleted
- 3 × `Class1.cs` placeholders (Domain, Application, Infrastructure)

---

## Test Results

```
Passed!  - Failed: 0, Passed:  15 - Evaluation (NEW)
Passed!  - Failed: 0, Passed:  69 - AgentRuns
Passed!  - Failed: 0, Passed:  14 - Reporting
Passed!  - Failed: 0, Passed: 320 - SafeActions
Passed!  - Failed: 0, Passed:  24 - Integration
Passed!  - Failed: 0, Passed:   8 - MCP Contract
Passed!  - Failed: 0, Passed:  31 - AlertIngestion
─────────────────────────────────────
Total:   481 passed, 0 failed
```

---

## Design Notes

- **No persistence**: All scenarios are deterministic in-memory checks — no database, no external I/O.
- **No LLM**: Scenarios verify domain logic rules only, not AI-generated content.
- **No new NuGet packages**: Only added `Microsoft.Extensions.DependencyInjection.Abstractions 9.0.2` (already used by all other Application layers).
- **No Worker/MCP changes**: Module is ApiHost-only.
- **Extensibility**: New scenarios are added by implementing `IEvaluationScenario` and registering in DI.
