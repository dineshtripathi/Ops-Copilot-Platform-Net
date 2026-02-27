# Slice 28 — Tenancy + Governance Config Store MVP — Evidence

## Summary

First **DB-backed** module in the Tenancy vertical. Introduces a `[tenancy]`
SQL schema with two tables (`Tenants`, `TenantConfigEntries`), EF Core
migrations, six CRUD + settings endpoints under `/tenants`, and a
governance-default fallback resolver that merges per-tenant overrides with
`Governance:Defaults` from `appsettings.json`.

---

## AC Checklist

| # | Acceptance Criteria | Status | Evidence |
|---|---------------------|--------|----------|
| 1 | EF migration creates `[tenancy].Tenants` and `[tenancy].TenantConfigEntries` tables | ✅ | `InitialCreate` migration in `Infrastructure/Migrations/` |
| 2 | `POST /tenants` creates a tenant → 201 Created | ✅ | `TenancyEndpoints.cs` — `MapPost("/")` |
| 3 | `GET /tenants` lists tenants → 200 OK | ✅ | `TenancyEndpoints.cs` — `MapGet("/")` |
| 4 | `GET /tenants/{id}` returns a single tenant → 200 / 404 | ✅ | `TenancyEndpoints.cs` — `MapGet("/{id:guid}")` |
| 5 | `PUT /tenants/{id}/settings` upserts a key-value config entry → 200 OK | ✅ | `TenancyEndpoints.cs` — `MapPut("/{id:guid}/settings")` |
| 6 | `GET /tenants/{id}/settings` returns raw per-tenant config entries → 200 OK | ✅ | `TenancyEndpoints.cs` — `MapGet("/{id:guid}/settings")` |
| 7 | `GET /tenants/{id}/settings/resolved` returns merged config (tenant overrides + governance defaults) → 200 OK | ✅ | `TenancyEndpoints.cs` — `MapGet("/{id:guid}/settings/resolved")` |
| 8 | `TenantConfigResolver` falls back to `GovernanceDefaultsConfig` for any key not overridden per-tenant | ✅ | `TenantConfigResolver.cs` — dict-based merge with `IOptions<GovernanceDefaultsConfig>` |
| 9 | Strict validation: displayName required, key ≤ 128 chars, value ≤ 1024 chars | ✅ | `TenancyEndpoints.cs` — inline validation with 400 returns |
| 10 | `UpdatedBy` captured from `x-identity` header on create, upsert | ✅ | `TenancyEndpoints.cs` — `request.HttpContext.Request.Headers["x-identity"]` |
| 11 | ≥ 12 new tests | ✅ | **17 tests** in `TenancyEndpointTests.cs` |
| 12 | All tests pass (≥ 524 total) | ✅ | **541 tests**, 0 failures |
| 13 | `.http` Section AD with requests for all 6 endpoints | ✅ | `OpsCopilot.Api.http` — Section AD (AD1–AD6) |
| 14 | Evidence document at `docs/dev-slice-28-evidence.md` | ✅ | This file |

---

## New Files (19)

| File | Layer |
|------|-------|
| `src/Modules/Tenancy/Domain/OpsCopilot.Tenancy.Domain/Entities/Tenant.cs` | Domain |
| `src/Modules/Tenancy/Domain/OpsCopilot.Tenancy.Domain/Entities/TenantConfigEntry.cs` | Domain |
| `src/Modules/Tenancy/Application/OpsCopilot.Tenancy.Application/Abstractions/ITenantRegistry.cs` | Application |
| `src/Modules/Tenancy/Application/OpsCopilot.Tenancy.Application/Abstractions/ITenantConfigStore.cs` | Application |
| `src/Modules/Tenancy/Application/OpsCopilot.Tenancy.Application/Abstractions/ITenantConfigResolver.cs` | Application |
| `src/Modules/Tenancy/Application/OpsCopilot.Tenancy.Application/Models/EffectiveTenantConfig.cs` | Application |
| `src/Modules/Tenancy/Application/OpsCopilot.Tenancy.Application/Configuration/GovernanceDefaultsConfig.cs` | Application |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Persistence/TenancyDbContext.cs` | Infrastructure |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Persistence/SqlTenantRegistry.cs` | Infrastructure |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Persistence/SqlTenantConfigStore.cs` | Infrastructure |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Services/TenantConfigResolver.cs` | Infrastructure |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Extensions/TenancyInfrastructureExtensions.cs` | Infrastructure |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Migrations/20250101000000_InitialCreate.cs` | Infrastructure |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Migrations/20250101000000_InitialCreate.Designer.cs` | Infrastructure |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Migrations/TenancyDbContextModelSnapshot.cs` | Infrastructure |
| `src/Modules/Tenancy/Presentation/OpsCopilot.Tenancy.Presentation/Endpoints/TenancyEndpoints.cs` | Presentation |
| `src/Modules/Tenancy/Presentation/OpsCopilot.Tenancy.Presentation/Extensions/TenancyPresentationExtensions.cs` | Presentation |
| `tests/Modules/Tenancy/OpsCopilot.Modules.Tenancy.Tests/TenancyEndpointTests.cs` | Tests |
| `tests/Modules/Tenancy/OpsCopilot.Modules.Tenancy.Tests/OpsCopilot.Modules.Tenancy.Tests.csproj` | Tests |

## Modified Files (7)

| File | Change |
|------|--------|
| `src/Modules/Tenancy/Domain/OpsCopilot.Tenancy.Domain/OpsCopilot.Tenancy.Domain.csproj` | Cleaned up (deleted `Class1.cs`) |
| `src/Modules/Tenancy/Application/OpsCopilot.Tenancy.Application/OpsCopilot.Tenancy.Application.csproj` | Domain project reference |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/OpsCopilot.Tenancy.Infrastructure.csproj` | EF Core + Options.ConfigurationExtensions packages, project references |
| `src/Modules/Tenancy/Presentation/OpsCopilot.Tenancy.Presentation/OpsCopilot.Tenancy.Presentation.csproj` | Application + Infrastructure project references |
| `src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj` | Tenancy.Presentation project reference |
| `src/Hosts/OpsCopilot.ApiHost/Program.cs` | `AddTenancyModule`, `UseTenancyMigrations`, `MapTenancyEndpoints` |
| `docs/http/OpsCopilot.Api.http` | Section AD — 6 requests for tenancy endpoints |

## Deleted Files (5)

| File | Reason |
|------|--------|
| `src/Modules/Tenancy/Domain/OpsCopilot.Tenancy.Domain/Class1.cs` | Placeholder replaced by real entities |
| `src/Modules/Tenancy/Application/OpsCopilot.Tenancy.Application/Class1.cs` | Placeholder replaced by real abstractions |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Class1.cs` | Placeholder replaced by real implementations |
| `src/Modules/Tenancy/Presentation/OpsCopilot.Tenancy.Presentation/Class1.cs` | Placeholder replaced by real endpoints |
| `src/Modules/Governance/Domain/OpsCopilot.Governance.Domain/Class1.cs` | Placeholder — empty module scaffolding |

---

## Test Results

```
Passed!  - Failed: 0, Passed:  30 - Connectors.Tests
Passed!  - Failed: 0, Passed:  69 - AgentRuns.Tests
Passed!  - Failed: 0, Passed:  15 - Evaluation.Tests
Passed!  - Failed: 0, Passed:  17 - Tenancy.Tests          ← NEW (+17)
Passed!  - Failed: 0, Passed:  27 - Reporting.Tests
Passed!  - Failed: 0, Passed: 320 - SafeActions.Tests
Passed!  - Failed: 0, Passed:  24 - Integration.Tests
Passed!  - Failed: 0, Passed:   8 - Mcp.ContractTests
Passed!  - Failed: 0, Passed:  31 - AlertIngestion.Tests
                              ───
                       Total: 541   (was 524)
```

---

## Design Decisions

- **`[tenancy]` schema**: Isolated DB schema keeps tenancy tables separate from other modules, following modular-monolith data-ownership boundaries.
- **Composite unique index**: `(TenantId, Key)` on `TenantConfigEntries` enables upsert-or-insert pattern without duplicates.
- **Dict-based merge in resolver**: `TenantConfigResolver` loads per-tenant entries into a dictionary, then falls back to `GovernanceDefaultsConfig` for missing keys — simple, testable, zero-allocation fallback.
- **`GovernanceDefaultsConfig` in Tenancy.Application**: The config class lives in the Tenancy module (not Governance) to avoid a cross-module assembly dependency while still binding to the `Governance:Defaults` config section.
- **TestServer + Moq pattern**: Tests use `WebApplication.CreateBuilder` → `UseTestServer` → inject mocks, matching the established project test pattern and avoiding EF InMemory for endpoint-level tests.
