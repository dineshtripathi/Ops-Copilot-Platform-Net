# Slice 35 — Packs Catalog Integration (Read-Only) — Evidence

## Deliverables

| # | Deliverable | Status | Notes |
|---|------------|--------|-------|
| A | Domain query projection records | ✅ | 4 sealed records in `Packs.Domain/Models/` |
| B | Extended IPackCatalog interface | ✅ | 7 new query methods added |
| C | PackCatalog indexes & query impl | ✅ | 3 OrdinalIgnoreCase dictionaries + all 8 methods |
| D | 5 new read-only catalog endpoints | ✅ | Search, Details, Runbooks, EvidenceCollectors, SafeActions |
| E | PackCatalogQueryTests (≥ 20 unit) | ✅ | 20 unit tests for catalog query logic |
| F | Endpoint integration tests (≥ 10) | ✅ | 11 integration tests for new endpoints |
| G | .http Section AL | ✅ | 7 entries (AL1–AL7) |
| H | Evidence doc | ✅ | This file |

## New Files (6 files)

### Domain Layer — Query Projections
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackDetails.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackRunbookSummary.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackEvidenceCollectorSummary.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackSafeActionSummary.cs`

### Tests
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackCatalogQueryTests.cs`
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PlatformPacksQueryEndpointTests.cs`

## Modified Files
- `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IPackCatalog.cs` — 7 new query methods
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackCatalog.cs` — full rewrite with 3 index dictionaries
- `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/Endpoints/PlatformPacksEndpoints.cs` — 5 new endpoint handlers
- `docs/http/OpsCopilot.Api.http` — Section AL (7 entries)

## Hard Constraints — Verified
- ❌ No SafeActions module changes
- ❌ No database changes
- ❌ No governance changes
- ❌ No new Azure calls
- ❌ No breaking changes to existing endpoints
- ✅ Read-only catalog queries only

## Domain Query Projections

| Record | Properties |
|--------|-----------|
| `PackDetails` | Name, Version, Description, ResourceTypes, MinimumMode, EvidenceCollectorCount, RunbookCount, SafeActionCount, IsValid, Errors, PackPath |
| `PackRunbookSummary` | Id, File |
| `PackEvidenceCollectorSummary` | Id, RequiredMode, QueryFile |
| `PackSafeActionSummary` | Id, RequiresMode, DefinitionFile |

## IPackCatalog — New Methods (7)

| Method | Return Type |
|--------|-------------|
| `GetByNameAsync(name, ct)` | `Task<LoadedPack?>` |
| `FindByResourceTypeAsync(resourceType, ct)` | `Task<IReadOnlyList<LoadedPack>>` |
| `FindByMinimumModeAsync(minimumMode, ct)` | `Task<IReadOnlyList<LoadedPack>>` |
| `GetDetailsAsync(name, ct)` | `Task<PackDetails?>` |
| `GetRunbooksAsync(name, ct)` | `Task<IReadOnlyList<PackRunbookSummary>?>` |
| `GetEvidenceCollectorsAsync(name, ct)` | `Task<IReadOnlyList<PackEvidenceCollectorSummary>?>` |
| `GetSafeActionsAsync(name, ct)` | `Task<IReadOnlyList<PackSafeActionSummary>?>` |

## PackCatalog Implementation

Three `Dictionary<string, …>(StringComparer.OrdinalIgnoreCase)` indexes built on first load:
- `_nameIndex` — `Dictionary<string, LoadedPack>` (Name → pack)
- `_resourceTypeIndex` — `Dictionary<string, List<LoadedPack>>` (resource type → packs)
- `_modeIndex` — `Dictionary<string, List<LoadedPack>>` (minimum mode → packs)

`EnsureLoadedAsync()` uses `SemaphoreSlim` with double-check pattern. Loader called exactly once.

## New Endpoints

| Route | Method | Description |
|-------|--------|-------------|
| `/reports/platform/packs/search` | GET | Search packs (optional `?resourceType=` or `?minimumMode=`) |
| `/reports/platform/packs/{name}` | GET | Get pack details by name (404 if not found) |
| `/reports/platform/packs/{name}/runbooks` | GET | Get runbook summaries for a pack |
| `/reports/platform/packs/{name}/evidence-collectors` | GET | Get evidence collector summaries |
| `/reports/platform/packs/{name}/safe-actions` | GET | Get safe action summaries |

Route ordering: `/packs/search` mapped **before** `/packs/{name}` to prevent route conflict.

## Test Coverage (31 new tests)

### PackCatalogQueryTests (20 unit tests)
1. GetByNameAsync — found
2. GetByNameAsync — case-insensitive match
3. GetByNameAsync — not found returns null
4. FindByResourceTypeAsync — matching packs
5. FindByResourceTypeAsync — case-insensitive
6. FindByResourceTypeAsync — no match returns empty
7. FindByMinimumModeAsync — matching packs
8. FindByMinimumModeAsync — no match returns empty
9. GetDetailsAsync — projects correctly
10. GetDetailsAsync — not found returns null
11. GetRunbooksAsync — returns summaries
12. GetRunbooksAsync — not found returns null
13. GetEvidenceCollectorsAsync — returns summaries
14. GetEvidenceCollectorsAsync — not found returns null
15. GetSafeActionsAsync — returns summaries
16. GetSafeActionsAsync — not found returns null
17. Multi-pack index returns all matches
18. Loader called only once across multiple queries
19–20. Additional edge cases

### PlatformPacksQueryEndpointTests (11 integration tests)
1. GetPackDetails — existing pack returns 200 with details
2. GetPackDetails — missing pack returns 404
3. SearchPacks — by resourceType returns filtered results
4. SearchPacks — by minimumMode returns filtered results
5. SearchPacks — no filters returns all packs
6. GetPackRunbooks — existing pack returns 200 with list
7. GetPackRunbooks — missing pack returns 404
8. GetPackEvidenceCollectors — existing pack returns 200 with list
9. GetPackEvidenceCollectors — missing pack returns 404
10. GetPackSafeActions — existing pack returns 200 with list
11. GetPackSafeActions — missing pack returns 404
