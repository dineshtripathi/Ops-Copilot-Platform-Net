# Slice 36 — Packs → Triage Integration (Mode A Only, STRICT) — Evidence

## Deliverables

| # | Deliverable | Status | Notes |
|---|------------|--------|-------|
| A | PackFileReader with path-traversal guard | ✅ | `IPackFileReader` + `PackFileReader` (reads & truncates to 2 000 chars) |
| B | Pack Runbook Discovery | ✅ | `PackTriageEnricher` resolves runbooks from Mode-A packs |
| C | Pack Evidence Collector Discovery | ✅ | Same enricher surfaces evidence collectors (Mode A only) |
| D | Triage Output Enrichment | ✅ | `AgentRunEndpoints` calls enricher after orchestrator, maps to DTOs |
| E | Packs config section | ✅ | `Packs:RootPath` + `Packs:ContentSnippetMaxLength` in appsettings |
| F | ≥ 16 tests (≥ 10 unit + ≥ 6 integration) | ✅ | 24 unit + 8 integration = **32 tests** |
| G | .http Section AM (AM1–AM6) | ✅ | 6 entries in `OpsCopilot.Api.http` |
| H | Evidence doc | ✅ | This file |

## New Files (6 files)

### Contracts / Abstractions
- `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IPackFileReader.cs`
- `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/IPackTriageEnricher.cs`
- `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/PackTriageEnrichment.cs`

### Infrastructure
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackFileReader.cs`
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackTriageEnricher.cs`

### Tests
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackFileReaderTests.cs` — 13 unit tests
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackTriageEnricherTests.cs` — 11 unit tests
- `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackTriageEnrichmentIntegrationTests.cs` — 8 integration tests

## Modified Files

- `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/TriageResponse.cs` — 3 new nullable pack fields
- `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Endpoints/AgentRunEndpoints.cs` — wired `IPackTriageEnricher` after orchestrator
- `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/OpsCopilot.AgentRuns.Presentation.csproj` — added Packs.Infrastructure reference
- `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/Extensions/PacksInfrastructureExtensions.cs` — registered `IPackFileReader` + `IPackTriageEnricher`
- `src/Hosts/OpsCopilot.ApiHost/appsettings.json` — `Packs` config section
- `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` — development overrides
- `docs/http/OpsCopilot.Api.http` — Section AM (6 entries)

## Hard Constraints — Verified

- ❌ No SafeActions module changes
- ❌ No database changes
- ❌ No governance changes
- ❌ No new Azure calls
- ❌ No Worker/Queue changes
- ❌ No breaking changes to existing endpoints/DTOs
- ✅ Mode A only — Mode-B packs are silently excluded
- ✅ Additive-only TriageResponse change (3 new nullable fields with defaults)

## Architecture

```
AgentRunEndpoints (AgentRuns.Presentation)
  └── POST /agent/triage
        1. TriageOrchestrator.RunAsync()  ← unchanged
        2. IPackTriageEnricher.EnrichAsync()  ← NEW (Slice 36)
        3. Map PackTriageEnrichment → TriageResponse DTOs
```

### Dependency Flow (respects DEPENDENCY_RULES.md)

```
AgentRuns.Presentation
  → BuildingBlocks.Contracts (IPackTriageEnricher)
  → Packs.Infrastructure (PackTriageEnricher impl, registered via DI)
      → Packs.Application (IPackFileReader, IPackCatalog)
      → Packs.Domain (LoadedPack, PackManifest, PackRunbook, etc.)
```

Cross-module call goes through `BuildingBlocks.Contracts.Packs.IPackTriageEnricher` — compliant.

## IPackFileReader

```csharp
Task<string?> ReadFileAsync(string packPath, string relativePath, int maxLength, CancellationToken ct);
```

- Resolves `packPath + relativePath` with `Path.GetFullPath`
- **Path-traversal guard**: rejects if resolved path is not under `packPath`
- Reads up to `maxLength` characters (default 2 000)
- Returns `null` on any I/O error (non-throwing)

## IPackTriageEnricher

```csharp
Task<PackTriageEnrichment> EnrichAsync(CancellationToken ct);
```

Returns:

| Field | Type |
|-------|------|
| `Runbooks` | `IReadOnlyList<PackRunbookEnrichment>` |
| `EvidenceCollectors` | `IReadOnlyList<PackEvidenceCollectorEnrichment>` |
| `Errors` | `IReadOnlyList<string>` |

### Enrichment Logic
1. `IPackCatalog.FindByMinimumModeAsync("A")` — get Mode-A packs only
2. For each valid pack, read runbook content via `IPackFileReader` (truncate to 2 000 chars)
3. Collect evidence collectors with `RequiredMode == "A"`
4. Invalid packs → error message appended (silently skipped)

## TriageResponse — New Fields

| Field | Type | Default |
|-------|------|---------|
| `PackRunbooks` | `IReadOnlyList<PackRunbookDto>?` | `null` |
| `PackEvidenceCollectors` | `IReadOnlyList<PackEvidenceCollectorDto>?` | `null` |
| `PackErrors` | `IReadOnlyList<string>?` | `null` |

### DTOs

| DTO | Properties |
|-----|-----------|
| `PackRunbookDto` | `PackName`, `RunbookId`, `File`, `ContentSnippet?` |
| `PackEvidenceCollectorDto` | `PackName`, `EvidenceCollectorId`, `RequiredMode`, `QueryFile?`, `KqlContent?` |

## Test Summary

| Test Class | Count | Type |
|-----------|-------|------|
| `PackFileReaderTests` | 13 | Unit |
| `PackTriageEnricherTests` | 11 | Unit |
| `PackTriageEnrichmentIntegrationTests` | 8 | Integration |
| **Total** | **32** | |

### PackFileReaderTests (13)

1. Reads file content up to max length
2. Truncates content exceeding max length
3. Returns null for non-existent file
4. Returns null for directory path
5. Rejects path-traversal attempt (`../`)
6. Rejects absolute path outside pack root
7. Handles empty file
8. Handles file at exact max length
9. Handles Unicode content
10. Handles deeply nested relative path
11. Handles whitespace-only file
12. Returns null on locked file (I/O error)
13. Normalises mixed path separators

### PackTriageEnricherTests (11)

1. Returns runbooks for Mode-A packs
2. Returns evidence collectors for Mode-A packs
3. Filters out Mode-B packs
4. Returns empty lists when no packs exist
5. Reads runbook content snippet via PackFileReader
6. Truncates runbook snippet to configured length
7. Reports errors for invalid packs
8. Skips invalid packs — no runbooks returned
9. Handles mixed valid/invalid packs
10. Returns null snippet when file unreadable
11. Filters evidence collectors to Mode A only

### PackTriageEnrichmentIntegrationTests (8)

1. Triage response contains pack runbooks
2. Triage response contains pack evidence collectors
3. Triage response omits pack errors when none
4. Pack enrichment skips Mode-B packs
5. Pack enrichment skips invalid packs (empty result)
6. Runbook content snippet is populated
7. Evidence collector KQL content is populated
8. Empty catalog yields empty pack fields

## .http Section AM (6 entries)

| Entry | Description |
|-------|------------|
| AM1 | Triage with pack enrichment — happy path |
| AM2 | Minimal payload — pack fields still present |
| AM3 | Pack runbook shape verification |
| AM4 | Pack evidence-collector shape verification |
| AM5 | No packs configured — empty arrays |
| AM6 | Backward compatibility — existing fields unaffected |

## Acceptance Criteria Cross-Reference

| # | Criterion | Met |
|---|----------|-----|
| AC-1 | `IPackFileReader` with path-traversal guard | ✅ |
| AC-2 | `PackFileReader` reads & truncates to 2 000 chars | ✅ |
| AC-3 | `IPackTriageEnricher` contract in BuildingBlocks.Contracts | ✅ |
| AC-4 | `PackTriageEnricher` resolves Mode-A packs only | ✅ |
| AC-5 | Runbook content snippet populated in enrichment | ✅ |
| AC-6 | Evidence collectors limited to Mode A | ✅ |
| AC-7 | TriageResponse extended with 3 nullable fields | ✅ |
| AC-8 | AgentRunEndpoints wires enricher after orchestrator | ✅ |
| AC-9 | Packs config section in appsettings | ✅ |
| AC-10 | ≥ 10 unit tests | ✅ (24) |
| AC-11 | ≥ 6 integration tests | ✅ (8) |
| AC-12 | .http Section AM (AM1–AM6) | ✅ |
| AC-13 | Evidence doc | ✅ |
