# Dev Slice 44 — Packs → SafeActions Proposals (Mode B/C, STRICT, No Auto-Execute)

## Summary

Slice 44 enables Packs to **propose SafeActions during triage** as read-only
recommendations. Proposals are informational only — never auto-approved, never
auto-executed, and never routed to the SafeActions execution pipeline.

A **double-gate pattern** controls proposal generation:

1. **Gate 1 — Deployment mode ≥ B** (`Packs:DeploymentMode` is `"B"` or `"C"`)
2. **Gate 2 — Feature flag** (`Packs:SafeActionsEnabled` is `"true"`)

Each individual safe action is further filtered: its `RequiresMode` must be ≤ the
current deployment mode. Because FileSystemPackLoader Rule 9 mandates that **all**
safeActions carry `requiresMode="C"`, proposals are effectively only produced when
`DeploymentMode` is `"C"`.

**Mode A behavior is unchanged.** No new routes, no DB schema changes, no breaking DTOs.

---

## Deliverables

### A — Contract interface + result records

| File | Layer | Description |
|------|-------|-------------|
| `IPackSafeActionProposer.cs` | Contracts/Packs | `ProposeAsync(PackSafeActionProposalRequest, CancellationToken)` |
| `PackSafeActionProposalResult.cs` | Contracts/Packs | 3 sealed records: `PackSafeActionProposalRequest`, `PackSafeActionProposalItem` (8 props), `PackSafeActionProposalResult(Items, Errors)` |

### B — PackSafeActionProposer implementation (240 lines)

`internal sealed class` in the Packs Infrastructure layer. Constructor takes 5 dependencies
(`IConfiguration`, `IPackCatalog`, `IWorkspaceRegistry`, `IPacksTelemetry`, `ILogger`).
`ProposeAsync` performs: Gate 1 → Gate 2 → catalog load → pack filter → per-action
`RequiresMode` filter → `BuildProposalItem`. Per-action errors are captured in the
`Errors` list without aborting the batch.

### C — Configuration

`Packs:SafeActionsEnabled` added to `appsettings.json` (`false`) and
`appsettings.Development.json` (`true`). Existing `Packs:DeploymentMode` is unchanged.

### D — DI wiring

`PacksInfrastructureExtensions.AddPacksInfrastructure()` registers
`AddSingleton<IPackSafeActionProposer, PackSafeActionProposer>()`.

### E — Triage endpoint integration

`AgentRunEndpoints.MapAgentRunEndpoints()` receives `IPackSafeActionProposer` via minimal
API DI injection (7th parameter at line 41). After existing triage logic, calls
`proposer.ProposeAsync(...)`, maps results to `PackSafeActionProposalDto`, and passes
them as the 14th parameter of `TriageResponse`.

### F — DTO + response extension

| File | Layer | Description |
|------|-------|-------------|
| `PackSafeActionProposalDto.cs` | AgentRuns/Presentation/Contracts | Public sealed record with 8 properties |
| `TriageResponse.cs` | AgentRuns/Presentation/Contracts | 14th optional parameter: `IReadOnlyList<PackSafeActionProposalDto>? PackSafeActionProposals = null` |

### G — .http Section AQ (AQ1–AQ6)

| Request | Scenario | Expected |
|---------|----------|----------|
| AQ1 | Mode C + SafeActionsEnabled=true | Proposals returned for RequiresMode=C actions |
| AQ2 | Mode B + SafeActionsEnabled=true | Empty proposals (Rule 9 blocks all) |
| AQ3 | Mode A | No proposals (Gate 1 blocks) |
| AQ4 | Mode C + SafeActionsEnabled=false | No proposals (Gate 2 blocks) |
| AQ5 | Mode B + SafeActionsEnabled=false | No proposals (both gates block) |
| AQ6 | Mode C + multi-pack | Proposals from all matching packs |

---

## Files Changed

| File | Change | Type |
|------|--------|------|
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/IPackSafeActionProposer.cs` | Contract interface — `ProposeAsync` | Created |
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/PackSafeActionProposalResult.cs` | 3 sealed records (Request, Item, Result) | Created |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionProposer.cs` | 240-line implementation — double-gate + per-action filter | Created |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/PackSafeActionProposalDto.cs` | DTO for HTTP response | Created |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionProposerTests.cs` | 18 unit tests | Created |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionProposerIntegrationTests.cs` | 10 integration tests | Created |
| `src/Hosts/OpsCopilot.ApiHost/appsettings.json` | Added `Packs:SafeActionsEnabled: false` | Modified |
| `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` | Added `Packs:SafeActionsEnabled: true` | Modified |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/Extensions/PacksInfrastructureExtensions.cs` | DI registration for IPackSafeActionProposer | Modified |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/TriageResponse.cs` | 14th param: `PackSafeActionProposals` | Modified |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/AgentRunEndpoints.cs` | DI injection + proposal call + DTO mapping | Modified |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/TriageEvidenceIntegrationTests.cs` | Added mock IPackSafeActionProposer in both host-builder blocks | Modified |
| `docs/http/OpsCopilot.Api.http` | TOC entry + Section AQ (AQ1–AQ6) appended | Modified |
| `docs/dev-slice-44-evidence.md` | This file | Created |

---

## Test Totals

```
dotnet build OpsCopilot.sln -warnaserror
  Build succeeded.  0 Warning(s)  0 Error(s)

Packs module tests:
  New unit tests           18/18
  New integration tests    10/10
  Existing (unchanged)    155/155
  ─────────────────────────────
  Packs total             183/183  ✓  0 failures

Full solution:
  Governance               31/31
  Connectors               30/30
  AlertIngestion           31/31
  SafeActions             368/368
  Tenancy                  17/17
  Reporting                27/27
  AgentRuns                81/81
  Evaluation               15/15
  Packs                   183/183
  Integration              24/24
  MCP Contract              8/8
  ─────────────────────────────
  Grand total             815/815  ✓  0 failures
```

---

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Double-gate pattern** (mode ≥ B AND SafeActionsEnabled) | Provides two independent kill switches — deployment mode for org-level control, feature flag for gradual rollout |
| **Per-action RequiresMode filter** | Future-proofs for mixed-mode packs where some actions need only Mode B |
| **Rule 9 forces RequiresMode="C"** | FileSystemPackLoader validation (hardcoded) makes Mode B proposals empty today; no code change needed when Rule 9 relaxes |
| **Read-only proposals — no execution** | STRICT constraint: proposals are informational only, never auto-approved or auto-executed |
| **14th optional param on TriageResponse** | Additive, non-breaking; existing consumers see `null` when deserializing older responses |
| **Error accumulation per-action** | Single bad action definition doesn't block proposals from other healthy actions in the same pack |
| **Mock IPackSafeActionProposer in AgentRuns tests** | Required for minimal API parameter resolution in WebApplicationFactory-based integration tests |

---

## Non-Negotiables Verification

| Constraint | Status |
|-----------|--------|
| No runtime behavior change (Mode A) | ✓ — Gate 1 rejects Mode A; existing path unchanged |
| No new routes | ✓ — Proposals returned inline in existing triage response |
| No DB schema / migration changes | ✓ |
| No breaking DTO changes | ✓ — Additive only: 14th param with default `null` |
| No SafeActions execution routing changes | ✓ — Proposals are read-only; no execution triggered |
| No background workers | ✓ |
| No new NuGet packages | ✓ |
| No Azure writes | ✓ — Proposal logic is local/offline |
| No secrets in logs | ✓ — Only IDs and mode strings in telemetry/logs |
| Mode A deterministic and offline | ✓ |
| 0 warnings / 0 errors build | ✓ |
| All tests pass (815/815) | ✓ |

*Not committed — awaiting explicit instruction.*
