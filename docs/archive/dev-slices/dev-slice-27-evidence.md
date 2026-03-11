# Slice 27 — Reporting Expansion (Evaluation + Connector Awareness) — Evidence

## Summary

Three new **read-only** GET endpoints under `/reports/platform/` exposing
evaluation summary, connector inventory, and platform readiness reports.
No database tables, no mutations — purely in-memory aggregation of existing
module services.

---

## AC Checklist

| # | Acceptance Criteria | Status | Evidence |
|---|---------------------|--------|----------|
| 1 | `GET /reports/platform/evaluation-summary` returns `EvaluationSummaryReport` (TotalScenarios, Passed, Failed, PassRate, Modules, Categories, GeneratedAtUtc) | ✅ | `PlatformReportingEndpoints.cs` — `/evaluation-summary` route |
| 2 | `GET /reports/platform/connectors` returns `ConnectorInventoryReport` (TotalConnectors, ByKind, Connectors[], GeneratedAtUtc) | ✅ | `PlatformReportingEndpoints.cs` — `/connectors` route |
| 3 | `GET /reports/platform/readiness` returns `PlatformReadinessReport` (EvaluationPassRate, TotalConnectors, TotalActionTypes, AllEvaluationsPassing, GeneratedAtUtc) | ✅ | `PlatformReportingEndpoints.cs` — `/readiness` route |
| 4 | Domain models are read-only records in `Reporting.Domain/Models/` | ✅ | `EvaluationSummaryReport.cs`, `ConnectorInventoryReport.cs`, `ConnectorInventoryRow.cs`, `PlatformReadinessReport.cs` — all `sealed record` |
| 5 | `IPlatformReportingQueryService` abstraction in `Reporting.Application/Abstractions/` | ✅ | Interface with 3 async methods |
| 6 | `PlatformReportingQueryService` in `Reporting.Infrastructure/Queries/` consumes `EvaluationRunner`, `EvaluationScenarioCatalog`, `IConnectorRegistry`, `IActionTypeCatalog` | ✅ | 97-line implementation, all in-memory |
| 7 | Cross-module references follow `DEPENDENCY_RULES.md` — only via `Connectors.Abstractions`, `Evaluation.Application`, `SafeActions.Application` | ✅ | `OpsCopilot.Reporting.Infrastructure.csproj` — 3 new `<ProjectReference>` |
| 8 | DI registration as singleton in `ReportingInfrastructureExtensions` | ✅ | `AddSingleton<IPlatformReportingQueryService, PlatformReportingQueryService>()` |
| 9 | Endpoints wired in `Program.cs` | ✅ | `app.MapPlatformReportingEndpoints();` added |
| 10 | ≥ 12 new tests covering all 3 endpoints | ✅ | **13 tests** in `PlatformReportingEndpointTests.cs` |
| 11 | All tests pass (≥ 523 total) | ✅ | **524 tests**, 0 failures |
| 12 | `.http` Section AC with 3 GET requests | ✅ | `OpsCopilot.Api.http` — Section AC (AC1–AC3) |
| 13 | Evidence document at `docs/dev-slice-27-evidence.md` | ✅ | This file |

---

## New Files (8)

| File | Layer |
|------|-------|
| `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/EvaluationSummaryReport.cs` | Domain |
| `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/ConnectorInventoryReport.cs` | Domain |
| `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/ConnectorInventoryRow.cs` | Domain |
| `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/PlatformReadinessReport.cs` | Domain |
| `src/Modules/Reporting/Application/OpsCopilot.Reporting.Application/Abstractions/IPlatformReportingQueryService.cs` | Application |
| `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/Queries/PlatformReportingQueryService.cs` | Infrastructure |
| `src/Modules/Reporting/Presentation/OpsCopilot.Reporting.Presentation/Endpoints/PlatformReportingEndpoints.cs` | Presentation |
| `tests/Modules/Reporting/OpsCopilot.Modules.Reporting.Tests/PlatformReportingEndpointTests.cs` | Tests |

## Modified Files (3)

| File | Change |
|------|--------|
| `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/OpsCopilot.Reporting.Infrastructure.csproj` | 3 new `<ProjectReference>` entries |
| `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/Extensions/ReportingInfrastructureExtensions.cs` | Singleton registration |
| `src/Hosts/OpsCopilot.ApiHost/Program.cs` | `app.MapPlatformReportingEndpoints()` wiring |

## Appended Section (1)

| File | Change |
|------|--------|
| `docs/http/OpsCopilot.Api.http` | Section AC — 3 GET requests for platform reporting |

---

## Test Results

```
Passed!  - Failed: 0, Passed:  30 - Connectors.Tests
Passed!  - Failed: 0, Passed:  69 - AgentRuns.Tests
Passed!  - Failed: 0, Passed:  15 - Evaluation.Tests
Passed!  - Failed: 0, Passed:  27 - Reporting.Tests        ← was 14, now 27 (+13)
Passed!  - Failed: 0, Passed: 320 - SafeActions.Tests
Passed!  - Failed: 0, Passed:  24 - Integration.Tests
Passed!  - Failed: 0, Passed:   8 - Mcp.ContractTests
Passed!  - Failed: 0, Passed:  31 - AlertIngestion.Tests
                              ───
                       Total: 524   (was 511)
```

---

## Design Decisions

- **Separate interface**: `IPlatformReportingQueryService` is deliberately separate from `IReportingQueryService` — the existing service is SQL/EF-based, while platform reporting is purely in-memory aggregation.
- **Singleton lifetime**: All injected dependencies (`EvaluationRunner`, `EvaluationScenarioCatalog`, `IConnectorRegistry`, `IActionTypeCatalog`) are effectively singletons.
- **No database**: All data is computed on-the-fly from existing in-memory services.
- **Read-only**: All domain models are `sealed record` with no mutation methods.

---

*Not committed — awaiting explicit instruction.*
