# Slice 37 — Packs → Triage Dependency Hygiene (STRICT, No Behavior Change)

## Goal

Remove the supposed layering smell where `AgentRuns.Presentation` directly
references `Packs.Infrastructure`. After this slice, `AgentRuns.Presentation`
depends only on `BuildingBlocks.Contracts` for `IPackTriageEnricher`, with the
composition root (`ApiHost`) handling all wiring.

**STRICT constraints**: No new routes, no DTO changes, no schema changes, no
behavior changes, no new packages — wiring + references only.

---

## Key Finding

**No code changes were required.**

Investigation revealed that `OpsCopilot.AgentRuns.Presentation.csproj` **never
had** a `Packs.Infrastructure` project reference. The Slice 36 evidence doc
incorrectly documented "added Packs.Infrastructure reference", but the actual
implementation was architecturally correct from the start:

- `AgentRunEndpoints.cs` uses only `IPackTriageEnricher` from
  `OpsCopilot.BuildingBlocks.Contracts.Packs` (interface injection).
- The composition root (`Program.cs`) calls `.AddPacksModule(builder.Configuration)`
  which chains to `AddPacksInfrastructure`, registering `IPackTriageEnricher` as
  a singleton.

---

## Files Verified (NOT Modified)

| File | Verification |
|------|-------------|
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/OpsCopilot.AgentRuns.Presentation.csproj` | No `Packs.Infrastructure` reference. References: AgentRuns.Application, AgentRuns.Domain, AgentRuns.Infrastructure, BuildingBlocks.Domain, BuildingBlocks.Contracts. |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Endpoints/AgentRunEndpoints.cs` | Only pack-related `using` is `OpsCopilot.BuildingBlocks.Contracts.Packs`. Injects `IPackTriageEnricher` as interface parameter — no concrete Packs types. |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Extensions/AgentRunsPresentationExtensions.cs` | `AddAgentRunsModule` calls only `AddAgentRunsApplication()` + `AddAgentRunsInfrastructure()`. No Packs references. |
| `src/Hosts/OpsCopilot.ApiHost/Program.cs` | `.AddPacksModule(builder.Configuration)` called in service registration chain before `app.Build()`. |
| `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/Extensions/PacksPresentationExtensions.cs` | `AddPacksModule` → `AddPacksApplication()` + `AddPacksInfrastructure(configuration)`. |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/Extensions/PacksInfrastructureExtensions.cs` | `AddPacksInfrastructure` registers `IPackFileReader`, `IPackCatalog`, `IPackTriageEnricher` as singletons. |

### Additional Verification

- `grep Packs.Infrastructure **/*.csproj` → found ONLY in `Packs.Tests.csproj`
  and `Packs.Presentation.csproj` (expected, same-module references).
- `grep "using OpsCopilot.Packs" src/Modules/AgentRuns/**` → **0 matches**.
  AgentRuns has zero imports from any `Packs.*` namespace.

---

## Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| AC-1 | `AgentRuns.Presentation.csproj` does NOT contain a `ProjectReference` to `Packs.Infrastructure` | ✅ Already satisfied — reference never existed |
| AC-2 | `AgentRuns.Presentation` compiles using only `IPackTriageEnricher` from `BuildingBlocks.Contracts` | ✅ Already satisfied |
| AC-3 | `ApiHost/Program.cs` registers Packs services via `.AddPacksModule(builder.Configuration)` | ✅ Already satisfied |
| AC-4 | Triage endpoint returns identical output (no behavior change) | ✅ No code changed |
| AC-5 | No route, DTO, or schema changes | ✅ No code changed |
| AC-6 | All existing tests still pass | ✅ 721 / 721 |
| AC-7 | `dotnet build -warnaserror` → 0 warnings, 0 errors | ✅ Confirmed |
| AC-8 | `dotnet test` → all green | ✅ 721 passed, 0 failed |
| AC-9 | Evidence doc committed | ✅ This document |

---

## Build & Test Gate

```
dotnet build OpsCopilot.sln -warnaserror
  Build succeeded.
  0 Warning(s)
  0 Error(s)

dotnet test OpsCopilot.sln --no-build --verbosity minimal
  Passed! — 11 assemblies, 721 tests, 0 failures

  Connectors.Tests ......... 30
  Governance.Tests ......... 31
  Evaluation.Tests ......... 15
  AgentRuns.Tests .......... 69
  AlertIngestion.Tests ..... 31
  Reporting.Tests .......... 27
  Packs.Tests ............. 101
  Tenancy.Tests ............ 17
  SafeActions.Tests ....... 368
  Integration.Tests ........ 24
  Mcp.ContractTests ......... 8
                            ---
  Total                     721
```

---

## Summary

Slice 37 is a **verification-only** slice. The layering concern flagged in the
Slice 36 evidence doc did not manifest in the actual code — `AgentRuns.Presentation`
was correctly wired from the start, depending only on `BuildingBlocks.Contracts`
for the `IPackTriageEnricher` interface. All 9 acceptance criteria are satisfied
with zero code changes.
