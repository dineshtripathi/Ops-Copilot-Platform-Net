# Slice 23 — Reporting MVP (STRICT, Read-Only, No Schema Changes)

## Evidence Document

| Item | Detail |
| --- | --- |
| **Slice** | 23 |
| **Title** | Reporting MVP |
| **Branch** | main |
| **Baseline commit** | `3801127` (Slice 22) |
| **Baseline tests** | 421 |
| **Final tests** | 435 (421 + 14 new) |
| **Status** | All acceptance criteria met |

---

## Acceptance Criteria

| AC | Description | Status | Evidence |
| --- | --- | --- | --- |
| AC-1 | `GET /reports/safe-actions/summary` returns 200 with aggregated totals | ✅ | `ReportingEndpoints.cs` `/summary` route; Tests 1-3 |
| AC-2 | `GET /reports/safe-actions/by-action-type` returns 200 with breakdown | ✅ | `ReportingEndpoints.cs` `/by-action-type` route; Tests 7-8 |
| AC-3 | `GET /reports/safe-actions/by-tenant` returns 200 with per-tenant rows | ✅ | `ReportingEndpoints.cs` `/by-tenant` route; Test 9 |
| AC-4 | `GET /reports/safe-actions/recent` returns 200 with recent action rows | ✅ | `ReportingEndpoints.cs` `/recent` route; Tests 10-14 |
| AC-5 | Optional `x-tenant-id` header filters summary, by-action-type, recent | ✅ | Tests 3, 8, 13 verify tenant header passed to service |
| AC-6 | `/by-tenant` ignores tenant header (leadership cross-tenant view) | ✅ | Endpoint does not extract tenant; Test 9 proves no tenant param |
| AC-7 | Date range params `fromUtc`/`toUtc` validated; 400 on bad input | ✅ | `TryParseDateRange` helper; Tests 4, 5, 6 |
| AC-8 | `fromUtc` after `toUtc` returns 400 | ✅ | Test 6 |
| AC-9 | `/recent` default limit = 20, max = 100, clamped via `Math.Clamp` | ✅ | Tests 10 (default 20), 11 (custom 5), 12 (clamp 999→100) |
| AC-10 | All endpoints read-only, no writes or schema changes | ✅ | No EF migrations; `ReportingReadDbContext` uses `NoTracking`; query-only service |
| AC-11 | Reporting module uses own `ReportingReadDbContext`, does NOT reference SafeActions.Infrastructure | ✅ | No cross-module infrastructure reference in any .csproj |
| AC-12 | Module wired into `Program.cs` via `AddReportingModule` + `MapReportingEndpoints` | ✅ | `Program.cs` updated |
| AC-13 | ≥12 new unit tests | ✅ | 14 tests in `ReportingEndpointTests.cs` |
| AC-14 | All 435 tests pass (0 failures) | ✅ | `dotnet test` output: 69+14+320+24+8 = 435 |
| AC-15 | `.http` Section Y added with ≥6 requests | ✅ | Section Y: requests Y1–Y8 |

---

## New / Modified Files

### Domain (4 new files)
- `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/SafeActionsSummaryReport.cs`
- `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/ActionTypeBreakdownRow.cs`
- `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/TenantBreakdownRow.cs`
- `src/Modules/Reporting/Domain/OpsCopilot.Reporting.Domain/Models/RecentActionRow.cs`

### Application (2 new files + 1 modified)
- `src/Modules/Reporting/Application/OpsCopilot.Reporting.Application/Abstractions/IReportingQueryService.cs`
- `src/Modules/Reporting/Application/OpsCopilot.Reporting.Application/Extensions/ReportingApplicationExtensions.cs`
- `src/Modules/Reporting/Application/OpsCopilot.Reporting.Application/OpsCopilot.Reporting.Application.csproj` (added DI package + Domain ref)

### Infrastructure (4 new files + 1 modified)
- `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/Persistence/ReadModels/ActionRecordReadModel.cs`
- `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/Persistence/ReportingReadDbContext.cs`
- `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/Queries/ReportingQueryService.cs`
- `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/Extensions/ReportingInfrastructureExtensions.cs`
- `src/Modules/Reporting/Infrastructure/OpsCopilot.Reporting.Infrastructure/OpsCopilot.Reporting.Infrastructure.csproj` (EF Core + refs)

### Presentation (2 new files + 1 modified)
- `src/Modules/Reporting/Presentation/OpsCopilot.Reporting.Presentation/Endpoints/ReportingEndpoints.cs`
- `src/Modules/Reporting/Presentation/OpsCopilot.Reporting.Presentation/Extensions/ReportingPresentationExtensions.cs`
- `src/Modules/Reporting/Presentation/OpsCopilot.Reporting.Presentation/OpsCopilot.Reporting.Presentation.csproj` (FrameworkReference + project refs)

### ApiHost (2 modified)
- `src/Hosts/OpsCopilot.ApiHost/Program.cs` (added wiring)
- `src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj` (added Reporting.Presentation reference)

### Tests (2 new/modified)
- `tests/Modules/Reporting/OpsCopilot.Modules.Reporting.Tests/OpsCopilot.Modules.Reporting.Tests.csproj` (full test project)
- `tests/Modules/Reporting/OpsCopilot.Modules.Reporting.Tests/ReportingEndpointTests.cs` (14 tests)

### .http
- `docs/http/OpsCopilot.Api.http` (Section Y: Y1–Y8)

### Deleted
- 5 × `Class1.cs` placeholders (Domain, Application, Infrastructure, Presentation, Tests)

---

## Test Results

```
Passed!  - Failed: 0, Passed:  69 - AgentRuns
Passed!  - Failed: 0, Passed:  14 - Reporting (NEW)
Passed!  - Failed: 0, Passed: 320 - SafeActions
Passed!  - Failed: 0, Passed:  24 - Integration
Passed!  - Failed: 0, Passed:   8 - MCP Contract
─────────────────────────────────────
Total:   435 passed, 0 failed
```
