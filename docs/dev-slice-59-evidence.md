# Dev Slice 59 — Evidence: Vector-Backed Incident Memory

## Objective
Make incident memory real, tenant-safe, citation-backed, and honest.  
Replace the no-op `NullIncidentMemoryRetrievalService` with a working vector-store-backed
implementation that indexes agent-run summaries and returns scored, tenant-scoped recall hits.

---

## Pre-Flight Checks

| ID | Check | Finding | Status |
|----|-------|---------|--------|
| PF-1 | Domain model — `IncidentMemoryDocument` | New entity in `OpsCopilot.Rag.Domain` with `[VectorStoreKey]`, `[VectorStoreData]`, `[VectorStoreVector(1536)]` attributes; includes `TenantId`, `RunId`, `AlertFingerprint`, `SummaryText`, `CreatedAtUtc` | ✅ Pass |
| PF-2 | Application interface shape | `IIncidentMemoryRetrievalService.SearchAsync(IncidentMemoryQuery) → Task<IReadOnlyList<IncidentMemoryHit>>` — thin, single-method contract | ✅ Pass |
| PF-3 | Application indexer interface | `IIncidentMemoryIndexer.IndexAsync(IncidentMemoryRecord, CancellationToken)` — one-method write path | ✅ Pass |
| PF-4 | Tenant isolation | TenantId compared on every returned `VectorSearchResult` record; mismatched tenant → skip. No cross-tenant data can surface in results | ✅ Pass |
| PF-5 | Score gate | `r.Score < query.MinScore` → skip; default `MinScore = 0.7` | ✅ Pass |
| PF-6 | Summary truncation | `SummaryText.Length <= 200 ? SummaryText : SummaryText[..200]` — SummarySnippet always ≤ 200 chars | ✅ Pass |
| PF-7 | Error containment | `catch (Exception)` in `VectorIncidentMemoryRetrievalService` logs at Warning and returns `Array.Empty<IncidentMemoryHit>()` — embedding failures never propagate to triage | ✅ Pass |
| PF-8 | AgentRuns integration | `TriageOrchestrator` already accepts `IIncidentMemoryRetrievalService?` (nullable); `RagBackedIncidentMemoryRetrievalService` wraps RAG hits as citations | ✅ Pass |
| PF-9 | InternalsVisibleTo | Both `Rag.Infrastructure` and `AgentRuns.Infrastructure` `AssemblyInfo.cs` grant test project access | ✅ Pass |
| PF-10 | Dependency rules | All new types placed in correct layers: Domain → Application interfaces → Infrastructure implementations. No upward dependencies. | ✅ Pass |

---

## Files Created / Modified

### Domain
| Path | Change |
|------|--------|
| `src/Modules/Rag/OpsCopilot.Rag.Domain/IncidentMemoryDocument.cs` | New — vector store entity |

### Application
| Path | Change |
|------|--------|
| `src/Modules/Rag/OpsCopilot.Rag.Application/Memory/IIncidentMemoryRetrievalService.cs` | New — read interface |
| `src/Modules/Rag/OpsCopilot.Rag.Application/Memory/IIncidentMemoryIndexer.cs` | New — write interface |
| `src/Modules/Rag/OpsCopilot.Rag.Application/Memory/IncidentMemoryQuery.cs` | New — query value object |
| `src/Modules/Rag/OpsCopilot.Rag.Application/Memory/IncidentMemoryRecord.cs` | New — indexer input value object |
| `src/Modules/Rag/OpsCopilot.Rag.Application/Memory/IncidentMemoryHit.cs` | New — result value object |

### Infrastructure (Rag)
| Path | Change |
|------|--------|
| `src/Modules/Rag/OpsCopilot.Rag.Infrastructure/Memory/VectorIncidentMemoryRetrievalService.cs` | New — vector-backed retrieval |
| `src/Modules/Rag/OpsCopilot.Rag.Infrastructure/Memory/VectorIncidentMemoryIndexer.cs` | New — embedding + upsert |
| `src/Modules/Rag/OpsCopilot.Rag.Infrastructure/Memory/InMemoryIncidentMemoryRetrievalService.cs` | New — dev/test no-op fallback |
| `src/Modules/Rag/OpsCopilot.Rag.Infrastructure/OpsCopilot.Rag.Infrastructure.csproj` | Modified — added InternalsVisibleTo, VectorData/AI package refs |

### Infrastructure (AgentRuns)
| Path | Change |
|------|--------|
| `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Infrastructure/Memory/RagBackedIncidentMemoryRetrievalService.cs` | New — wraps RAG hits as citations |
| `src/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Infrastructure/Memory/NullIncidentMemoryRetrievalService.cs` | New — explicit null-object |

### Tests
| Path | Change |
|------|--------|
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/IncidentMemoryTests.cs` | New — 4 AgentRuns integration tests |
| `tests/Modules/Rag/OpsCopilot.Modules.Rag.Tests/IncidentMemoryRetrievalServiceTests.cs` | New — 4 Rag unit tests |
| `tests/Modules/Rag/OpsCopilot.Modules.Rag.Tests/OpsCopilot.Modules.Rag.Tests.csproj` | New — full csproj with all package/project refs |

---

## Test Inventory

### `OpsCopilot.Modules.Rag.Tests` — 4 tests

| Test | Scenario | Expected |
|------|----------|----------|
| `InMemoryService_SearchAsync_ReturnsEmpty` | `InMemoryIncidentMemoryRetrievalService` called | Returns empty list |
| `VectorService_SearchAsync_FiltersWrongTenant` | Doc TenantId ≠ query TenantId, score 0.95 | Returns empty (tenant mismatch) |
| `VectorService_SearchAsync_FiltersLowScore` | Doc TenantId matches, score 0.5 < MinScore 0.7 | Returns empty (score below threshold) |
| `VectorService_SearchAsync_EmbedderThrows_ReturnsEmpty` | `IEmbeddingGenerator` throws `InvalidOperationException` | Returns empty (catch block swallows, no throw) |

### `OpsCopilot.Modules.AgentRuns.Tests` — 4 tests (in `IncidentMemoryTests.cs`)

| Test | Scenario | Expected |
|------|----------|----------|
| `NullService_RecallAsync_ReturnsEmpty` | `NullIncidentMemoryRetrievalService` | Returns empty |
| `RagBackedService_RecallAsync_MapsHitsToCitations` | RAG returns 1 hit | 1 citation mapped with correct fields |
| `RagBackedService_RecallAsync_ReturnsEmpty_WhenNoHits` | RAG returns empty | Empty citations list |
| `TriageOrchestrator_AcceptsNullMemory_DoesNotThrow` | Null `IIncidentMemoryRetrievalService?` injected | No NullReferenceException |

---

## Build Gate

```
dotnet build .\OpsCopilot.sln --configuration Release -warnaserror
```

**Result:** `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Test Gate

```
dotnet test .\OpsCopilot.sln --no-build --configuration Release
```

**Result (selected assemblies):**

| Assembly | Passed | Failed |
|----------|--------|--------|
| OpsCopilot.Modules.Rag.Tests | 4 | 0 |
| OpsCopilot.Modules.AgentRuns.Tests | 126 | 0 |
| OpsCopilot.Modules.SafeActions.Tests | 368 | 0 |
| OpsCopilot.Modules.Packs.Tests | 303 | 0 |
| All assemblies | **987** | **0** |

---

## Design Notes

- **Thin and boring** — no new abstractions beyond what the slice required; `VectorStoreCollection<Guid, IncidentMemoryDocument>` used directly.
- **Tenant isolation is structural** — every result from `SearchAsync` is checked against `query.TenantId` at the record level; no filtering happens inside the vector store itself, eliminating dependence on vector-store-specific filter implementations.
- **Score gate default** — `MinScore = 0.7` matches the convention used in the existing `RunbookRetrievalService`.
- **Summary truncation** — 200-char cap prevents runaway context growth in triage prompts.
- **`FakeCollection`** — hand-rolled async-enumerable fake avoids Moq limitations with `IAsyncEnumerable`; all 11 abstract members of `VectorStoreCollection<TKey, TRecord>` implemented.
