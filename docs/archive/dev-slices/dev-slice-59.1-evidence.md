# Slice 59.1 — AI Package Alignment / Upgrade Safety

## Objective
Safely align AI-related NuGet packages to the latest stable 9.x patch versions.
This is a dependency hygiene slice, not a product feature.

---

## Pre-flight Verification

### PF-1: Package Inventory (before changes)

| Package | Old Version | Projects affected |
|---|---|---|
| `Microsoft.Extensions.AI` | 9.5.0 | AgentRuns.Application, AgentRuns.Infrastructure |
| `Microsoft.Extensions.AI.Abstractions` | 9.5.0 | Rag.Application, Rag.Infrastructure, Modules.Rag.Tests |
| `Microsoft.Extensions.VectorData.Abstractions` | 9.5.0 | Rag.Domain, Rag.Application, Rag.Infrastructure, Modules.Rag.Tests |

### PF-2: Target Versions

| Package | Latest 9.x stable | Latest 10.x stable | Decision |
|---|---|---|---|
| `Microsoft.Extensions.AI` | **9.10.2** | 10.4.0 | Cohort A — apply |
| `Microsoft.Extensions.AI.Abstractions` | **9.10.2** | 10.4.0 | Cohort A — apply |
| `Microsoft.Extensions.VectorData.Abstractions` | **9.7.0** | 10.0.1 | Cohort A — apply |

### PF-3: API Surface Review (GA status confirmed)

All call sites use fully GA, non-experimental APIs:
- `IChatClient.GetResponseAsync(messages, options, ct)` — AgentRuns orchestrator
- `IEmbeddingGenerator<string,Embedding<float>>.GenerateAsync(inputs, options, ct)` — Rag indexer + retrieval
- `VectorStoreCollection<TKey,TRecord>.UpsertAsync(record, ct)` — Rag indexer
- `VectorStoreCollection<TKey,TRecord>.SearchAsync(vector, top, options, ct)` — Rag retrieval

No `[Experimental]` attributes found. No `#pragma warning disable` suppression for preview APIs found. All APIs confirmed GA in 9.10.2.

### PF-4: FakeCollection Risk Assessment

`FakeCollection : VectorStoreCollection<Guid, IncidentMemoryDocument>` in the Rag test project implements 11 abstract overrides. Upgrading `VectorData.Abstractions` within 9.x (9.5.0 → 9.7.0) is a minor patch; no new abstract members were introduced. Risk: **low**. Result: all 4 Rag tests passed.

### PF-5: Cohort B — Deferred

Upgrading to `10.x` (AI → 10.4.0, VectorData → 10.0.1) requires verifying cross-major API compatibility, FakeCollection override surface, and potential breaking changes. Deferred to a dedicated slice.

---

## Changes Applied

### Cohort A: AI Package Version Bumps

| File | Package | Old | New |
|---|---|---|---|
| `AgentRuns.Application.csproj` | `Microsoft.Extensions.AI` | 9.5.0 | **9.10.2** |
| `AgentRuns.Infrastructure.csproj` | `Microsoft.Extensions.AI` | 9.5.0 | **9.10.2** |
| `Rag.Application.csproj` | `Microsoft.Extensions.AI.Abstractions` | 9.5.0 | **9.10.2** |
| `Rag.Application.csproj` | `Microsoft.Extensions.VectorData.Abstractions` | 9.5.0 | **9.7.0** |
| `Rag.Domain.csproj` | `Microsoft.Extensions.VectorData.Abstractions` | 9.5.0 | **9.7.0** |
| `Rag.Infrastructure.csproj` | `Microsoft.Extensions.AI.Abstractions` | 9.5.0 | **9.10.2** |
| `Rag.Infrastructure.csproj` | `Microsoft.Extensions.VectorData.Abstractions` | 9.5.0 | **9.7.0** |
| `Modules.Rag.Tests.csproj` | `Microsoft.Extensions.AI.Abstractions` | 9.5.0 | **9.10.2** |
| `Modules.Rag.Tests.csproj` | `Microsoft.Extensions.VectorData.Abstractions` | 9.5.0 | **9.7.0** |

### Co-dependency Floor Bumps (Necessary — NU1605 Fix)

`Microsoft.Extensions.AI 9.10.2` transitively requires `Microsoft.Extensions.DependencyInjection.Abstractions >= 9.0.10` and `Microsoft.Extensions.Logging.Abstractions >= 9.0.10`. Projects that directly pinned these below that floor produced NU1605 "downgrade detected" errors (treated as build errors via `-warnaserror`).

These are **patch-level security/bug-fix bumps** mandated by the transitive floor — not independent scope expansion.

| File | Package | Old | New |
|---|---|---|---|
| `AgentRuns.Application.csproj` | `Microsoft.Extensions.DependencyInjection.Abstractions` | 9.0.5 | **9.0.10** |
| `AgentRuns.Application.csproj` | `Microsoft.Extensions.Logging.Abstractions` | 9.0.5 | **9.0.10** |
| `Rag.Infrastructure.csproj` | `Microsoft.Extensions.DependencyInjection.Abstractions` | 9.0.2 | **9.0.10** |
| `Rag.Infrastructure.csproj` | `Microsoft.Extensions.Logging.Abstractions` | 9.0.2 | **9.0.10** |
| `Modules.Rag.Tests.csproj` | `Microsoft.Extensions.Logging.Abstractions` | 9.0.2 | **9.0.10** |

---

## Build Gate

```
dotnet build OpsCopilot.sln --configuration Release --no-incremental -warnaserror
```

**Result: Build succeeded. 0 Warning(s). 0 Error(s).**

---

## Test Gate

```
dotnet test OpsCopilot.sln --configuration Release --no-build
```

| Assembly | Passed | Failed |
|---|---|---|
| Modules.Governance.Tests | 31 | 0 |
| Modules.Rag.Tests | 4 | 0 |
| Modules.Connectors.Tests | 30 | 0 |
| Modules.Evaluation.Tests | 15 | 0 |
| Modules.AlertIngestion.Tests | 31 | 0 |
| Modules.Reporting.Tests | 27 | 0 |
| Modules.AgentRuns.Tests | 126 | 0 |
| Modules.Packs.Tests | 303 | 0 |
| Modules.Tenancy.Tests | 17 | 0 |
| Modules.SafeActions.Tests | 368 | 0 |
| Integration.Tests | 24 | 0 |
| Mcp.ContractTests | 8 | 0 |
| **Total** | **988** | **0** |

**Result: All 988 tests passed. 0 failed. 0 skipped.**

---

## What Was NOT Changed

- No 10.x upgrades applied (Cohort B deferred — dedicated slice required)
- No HTTP routes added or changed
- No DB schema or migration changes
- No new background workers or queues
- No runtime behavior changes
- Projects not in the AI/Rag dependency chain (e.g., Packs, Governance, Connectors, BuildingBlocks) were not modified
