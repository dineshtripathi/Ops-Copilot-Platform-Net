# Slice 34 — Packs Loader MVP — Evidence

## Deliverables

| # | Deliverable | Status | Notes |
|---|------------|--------|-------|
| A | Pack contracts (domain records) | ✅ | 6 sealed records in `Packs.Domain/Models/` |
| B | FileSystemPackLoader | ✅ | 175-line implementation with validation |
| C | Platform packs endpoint | ✅ | `GET /reports/platform/packs` |
| D | DI wiring | ✅ | Singleton loader + catalog, wired via `AddPacksModule` |
| E | ≥ 12 tests | ✅ | 19 tests (16 validation + 3 endpoint integration) |
| F | .http update | ✅ | Section AK added |
| G | Evidence doc | ✅ | This file |

## New Files (22 files)

### Domain Layer
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/OpsCopilot.Packs.Domain.csproj`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackManifest.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackActionType.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackGovernanceConfig.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackConnectorRef.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/PackValidationResult.cs`
- `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/LoadedPack.cs`

### Application Layer
- `src/Modules/Packs/Application/OpsCopilot.Packs.Application/OpsCopilot.Packs.Application.csproj`
- `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IPackLoader.cs`
- `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IPackCatalog.cs`
- `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Extensions/PacksApplicationExtensions.cs`

### Infrastructure Layer
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/OpsCopilot.Packs.Infrastructure.csproj`
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/FileSystemPackLoader.cs`
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackCatalog.cs`
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/Extensions/PacksInfrastructureExtensions.cs`

### Presentation Layer
- `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/OpsCopilot.Packs.Presentation.csproj`
- `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/Endpoints/PlatformPacksEndpoints.cs`
- `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/Extensions/PacksPresentationExtensions.cs`

### Sample Packs
- `packs/azure-vm/pack.json`
- `packs/k8s-basic/pack.json`

### Tests
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/OpsCopilot.Modules.Packs.Tests.csproj`
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/FileSystemPackLoaderValidationTests.cs`
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PlatformPacksEndpointTests.cs`

## Modified Files
- `src/Hosts/OpsCopilot.ApiHost/Program.cs` — added Packs module wiring
- `src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj` — added Packs.Presentation reference
- `OpsCopilot.sln` — added 4 Packs projects
- `docs/http/OpsCopilot.Api.http` — Section AK

## Hard Constraints — Verified
- ❌ No SafeActions changes
- ❌ No database changes
- ❌ No governance changes
- ❌ No new Azure calls
- ✅ Read-only pack loading only

## Test Coverage (19 tests)

### FileSystemPackLoaderValidationTests (16 tests)
1. Valid pack → IsValid, no errors
2. Missing/blank id → error
3. Invalid id pattern (uppercase, underscore, space, leading hyphen) → error
4. Id/directory mismatch → error
5. Missing/blank name → error
6. Missing/blank version → error
7. Invalid SemVer (various) → error
8. Valid SemVer with pre-release → passes
9. Description > 200 chars → error
10. Description exactly 200 chars → passes
11. Empty authors array → error
12. Null authors → error
13. Unknown action type → error
14. All 5 known action types → passes
15. Null actionTypes (optional) → passes
16. Multiple errors accumulated in single result

### PlatformPacksEndpointTests (3 tests)
1. Happy path — 200 OK with correct JSON shape (totalPacks, validPacks, invalidPacks, packs[])
2. Empty catalog — all counts zero
3. Mixed valid/invalid — correct valid/invalid split

## Endpoint

```
GET /reports/platform/packs
```

Returns:
```json
{
  "totalPacks": 2,
  "validPacks": 1,
  "invalidPacks": 1,
  "packs": [
    {
      "id": "azure-vm",
      "name": "Azure VM Pack",
      "version": "1.0.0",
      "description": "...",
      "authors": ["Platform Team"],
      "tags": ["azure", "vm"],
      "packPath": "/packs/azure-vm",
      "isValid": true,
      "errors": []
    }
  ]
}
```
