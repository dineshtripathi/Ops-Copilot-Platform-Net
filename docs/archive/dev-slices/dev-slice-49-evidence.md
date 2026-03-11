# Dev Slice 49 — Packs → SafeActions Proposal Record Creation (Mode C Only)

| Field | Value |
|-------|-------|
| **Status** | ✅ Complete |
| **Branch** | `main` |
| **Commit** | `e290f30` (`e290f3085b4be1d3e718058353e57d37bb7c8915`) |
| **Base Commit** | `0db7b2c` |
| **Date** | 2026-03-07 |

---

## 1. Objective

Add **Mode C–only** logic that bridges Pack safe-action proposals into real SafeAction proposal
records via `ISafeActionProposalService`. A new `PackSafeActionRecorder` creates one record per
executable, governance-allowed proposal. The result is surfaced as an additive
`PackSafeActionRecordSummary` on `TriageResponse`. No auto-approve / auto-execute; recommend-only.
No new routes were added — the feature is exposed via additive fields on the existing triage
response endpoint.

---

## 2. Files Changed

All paths verified from `git show --name-only e290f30`.

### Contracts — BuildingBlocks

| # | File | Change |
|---|------|--------|
| 1 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/IPackSafeActionRecorder.cs` | **New** — interface `IPackSafeActionRecorder` with `RecordAsync` method |
| 2 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/PackSafeActionRecordResult.cs` | **New** — records `PackSafeActionRecordRequest`, `PackSafeActionRecordItem`, `PackSafeActionRecordResult` |
| 3 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/SafeActions/ISafeActionProposalService.cs` | **New** — cross-module contract for proposal creation |
| 4 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/SafeActions/SafeActionProposalDeniedException.cs` | **New** — typed exception for policy-denied proposals |
| 5 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/SafeActions/SafeActionProposalResponse.cs` | **New** — response record returned by `ISafeActionProposalService.ProposeAsync` |

### Packs Module

| # | File | Change |
|---|------|--------|
| 6 | `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IPacksTelemetry.cs` | **Modified** — added 5 safe-action telemetry methods (Attempt, Created, Denied, Skipped, Failed) |
| 7 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/Extensions/PacksInfrastructureExtensions.cs` | **Modified** — added 8th DI registration: `IPackSafeActionRecorder → PackSafeActionRecorder` |
| 8 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionRecorder.cs` | **New** — recorder implementation: mode gate, feature gate, per-proposal loop (skip/create/deny/fail) |
| 9 | `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/Telemetry/PacksTelemetry.cs` | **Modified** — added 5 `Counter<long>` fields (`packs.safeaction.attempts/created/denied/skipped/failed`) |

### AgentRuns Module

| # | File | Change |
|---|------|--------|
| 10 | `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/TriageResponse.cs` | **Modified** — added `PackSafeActionRecordItemDto` (7 fields), `PackSafeActionRecordSummaryDto` (5 fields), 15th param `PackSafeActionRecordSummary = null` |
| 11 | `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Endpoints/AgentRunEndpoints.cs` | **Modified** — added 8th injection param `IPackSafeActionRecorder`, `RecordAsync` call, DTO mapping |

### SafeActions Module

| # | File | Change |
|---|------|--------|
| 12 | `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Extensions/SafeActionsApplicationExtensions.cs` | **Modified** — DI registration for `SafeActionProposalServiceAdapter` |
| 13 | `src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/SafeActionProposalServiceAdapter.cs` | **New** — adapter implementing `ISafeActionProposalService`, wired in SafeActions module |

### Tests

| # | File | Change |
|---|------|--------|
| 14 | `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/TriageEvidenceIntegrationTests.cs` | **Modified** — added mock `IPackSafeActionRecorder` DI registration to both test factory methods |
| 15 | `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionRecorderTests.cs` | **New** — 16 unit tests for `PackSafeActionRecorder` covering mode/feature gates, per-proposal outcomes, telemetry counters |

### Docs & Tooling

| # | File | Change |
|---|------|--------|
| 16 | `docs/http/OpsCopilot.Api.http` | **Modified** — TOC entry for Section AT + 5 new test cases (AT1–AT5) |
| 17 | `docs/dev-slice-49-evidence.md` | **New** — this evidence document |
| 18 | `.vscode/settings.json` | **Modified** — repo hygiene / editor settings (removed in Slice 50) |

---

## 3. Implementation Notes

### Cross-Module Bridge
`PackSafeActionRecorder` lives in `Packs.Infrastructure` but calls `ISafeActionProposalService`
(SafeActions module) via `IServiceScopeFactory.CreateScope()`, maintaining module isolation with
no direct project reference between Packs and SafeActions.

On the SafeActions side, `SafeActionProposalServiceAdapter` (in `SafeActions.Application/Orchestration`)
implements `ISafeActionProposalService` and is registered in `SafeActionsApplicationExtensions`.

### Mode & Feature Gates
- **Mode gate**: only Mode C (`char.ToUpperInvariant(mode[0]) == 'C'`) triggers recording.
  Modes A and B return an empty result with zero counts.
- **Feature gate**: `Packs:SafeActionsEnabled` config key must be `"true"`.
  Disabled → empty result, no service calls.

### Per-Proposal Logic
Each pack proposal is evaluated:
- **Not executable** → `Skipped` with reason `"not_executable"`
- **Governance denied** → `Skipped` with reason `"governance_denied"`
- **ProposeAsync succeeds** → `Created` with `ActionRecordId`
- **SafeActionProposalDeniedException** → `PolicyDenied` with `ReasonCode`
- **Generic exception** → `Failed` with error message

### Strict Posture
This feature is **recommend-only**. No auto-approve, no auto-execute. Proposals are created as
pending records; downstream approval/execution is out of scope for this slice.

### Telemetry
Five counters emitted per triage call: `RecordSafeActionAttempt`, `RecordSafeActionCreated`,
`RecordSafeActionSkipped`, `RecordSafeActionDenied`, `RecordSafeActionFailed`.

---

## 4. New Tests

### Unit Tests (PackSafeActionRecorderTests.cs) — 16 new

| # | Test Name | Scenario |
|---|-----------|----------|
| 1 | `RecordAsync_ModeA_ReturnsEmptyAndSkipsTelemetry` | Mode A gate → empty result, skip-telemetry emitted |
| 2 | `RecordAsync_ModeB_ReturnsEmpty` | Mode B gate → empty result |
| 3 | `RecordAsync_FeatureDisabled_ReturnsEmpty` | Feature gate off → empty result |
| 4 | `RecordAsync_EmptyProposals_ReturnsZeroCounts` | Zero proposals → zero counts, attempt telemetry |
| 5 | `RecordAsync_NonExecutable_Skipped` | Non-executable proposal → `Skipped` + `"not_executable"` |
| 6 | `RecordAsync_GovernanceDenied_Skipped` | Governance denied → `Skipped` + `"governance_denied"` |
| 7 | `RecordAsync_ProposeSucceeds_Created` | ProposeAsync OK → `Created` + `ActionRecordId` |
| 8 | `RecordAsync_ProposalDeniedException_PolicyDenied` | `SafeActionProposalDeniedException` → `PolicyDenied` + `ReasonCode` |
| 9 | `RecordAsync_GenericException_Failed` | Exception → `Failed` + error message |
| 10 | `RecordAsync_MixedResults_CorrectCounts` | 3 proposals (created + skipped + failed) → correct aggregation |
| 11 | `RecordAsync_Telemetry_Created` | Verifies `RecordSafeActionAttempt` + `RecordSafeActionCreated` |
| 12 | `RecordAsync_Telemetry_Skipped` | Verifies `RecordSafeActionSkipped` |
| 13 | `RecordAsync_Telemetry_Denied` | Verifies `RecordSafeActionDenied` |
| 14 | `RecordAsync_Telemetry_Failed` | Verifies `RecordSafeActionFailed` |
| 15 | `RecordAsync_NullParametersJson_DefaultsToEmptyObject` | Null `ParametersJson` → `"{}"` |
| 16 | `RecordAsync_LowercaseC_PassesGate` | Lowercase `"c"` → passes mode gate |

### .http Manual Tests (Section AT) — 5 requests

| # | Request | Purpose |
|---|---------|---------|
| AT1 | POST triage Mode C, happy path | `packSafeActionRecordSummary` populated with records, counts, errors |
| AT2 | POST triage Mode B | `packSafeActionRecordSummary` is null (mode gate) |
| AT3 | POST triage Mode C, feature disabled | `packSafeActionRecordSummary` is null |
| AT4 | POST triage Mode C, DTO shape | All 7 record-item fields + 4 summary fields present |
| AT5 | POST triage Mode A | `packSafeActionRecordSummary` is null (mode gate) |

---

## 5. Acceptance Criteria

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | `PackSafeActionRecorder` creates proposals only in Mode C | ✅ |
| AC-2 | Non-executable / governance-denied proposals skipped with reason codes | ✅ |
| AC-3 | `SafeActionProposalDeniedException` caught → `PolicyDenied` status | ✅ |
| AC-4 | Generic exceptions caught → `Failed` status, never throws | ✅ |
| AC-5 | Additive `PackSafeActionRecordSummary` on `TriageResponse` (default null) | ✅ |
| AC-6 | `PackSafeActionRecordItemDto` has 7 fields, `PackSafeActionRecordSummaryDto` has 5 fields | ✅ |
| AC-7 | Five telemetry counters emitted (attempt, created, skipped, denied, failed) | ✅ |
| AC-8 | Cross-module via `IServiceScopeFactory` — no direct project reference | ✅ |
| AC-9 | No new routes, no DB schema changes, no auto-execute | ✅ |
| AC-10 | No breaking DTO changes (additive only, `= null` default) | ✅ |
| AC-11 | ≥ 16 new unit tests | ✅ (16) |
| AC-12 | .http Section AT with ≥ 5 requests | ✅ (5) |
| AC-13 | Build 0W/0E, all tests green | ✅ |

---

## 6. Build & Test Evidence

```
dotnet build  → 0 Warning(s), 0 Error(s)
dotnet test   → 856 passed, 0 failed, 0 skipped (11 assemblies)
```

---

## 7. Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Cross-module call via DI scope | `IServiceScopeFactory.CreateScope()` — standard pattern, already used elsewhere |
| SafeAction service unavailable at runtime | All exceptions caught per-item; never bubbles to caller |
| Performance on large proposal lists | Loop is sequential and bounded by pack size (typically < 20 actions) |
| Config key missing | Feature gate defaults to disabled (safe) |
---

## 8. Reconciliation Note (Slice 50)

- Evidence doc corrected to match commit `e290f30` (added missing `PackSafeActionRecorderTests.cs`
  to Files Changed table, added base commit, renumbered Docs & Tooling rows 16–18).
- `.vscode/settings.json` removed for OSS hygiene (see Slice 50 evidence: `docs/dev-slice-50-evidence.md`).