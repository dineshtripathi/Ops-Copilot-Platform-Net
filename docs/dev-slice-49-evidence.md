# Dev Slice 49 — Packs → SafeActions Proposal Record Creation (Mode C Only)

| Field | Value |
|-------|-------|
| **Status** | ✅ Complete |
| **Branch** | `main` |
| **Base commit** | `0db7b2c` |
| **Date** | 2025-07-29 |

---

## 1. Objective

Add **Mode C–only** logic that bridges Pack safe-action proposals into real SafeAction proposal
records via `ISafeActionProposalService`. A new `PackSafeActionRecorder` creates one record per
executable, governance-allowed proposal. The result is surfaced as an additive
`PackSafeActionRecordSummary` on `TriageResponse`. No auto-approve / auto-execute; recommend-only.

---

## 2. Files Changed

| # | File | Change |
|---|------|--------|
| 1 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/IPackSafeActionRecorder.cs` | **New** — interface `IPackSafeActionRecorder` with `RecordAsync` method |
| 2 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/PackSafeActionRecordResult.cs` | **New** — records `PackSafeActionRecordRequest`, `PackSafeActionRecordItem`, `PackSafeActionRecordResult` |
| 3 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionRecorder.cs` | **New** — 209-line recorder: mode gate, feature gate, loop with skip/create/deny/fail per proposal |
| 4 | `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/IPacksTelemetry.cs` | **Modified** — added 5 safe-action telemetry methods (Attempt, Created, Denied, Skipped, Failed) |
| 5 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PacksTelemetry.cs` | **Modified** — added 5 `Counter<long>` fields (`packs.safeaction.attempts/created/denied/skipped/failed`) |
| 6 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PacksInfrastructureExtensions.cs` | **Modified** — added 8th DI registration: `IPackSafeActionRecorder → PackSafeActionRecorder` |
| 7 | `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/AgentRunEndpoints.cs` | **Modified** — added 8th injection param, `RecordAsync` call, DTO mapping, 15th arg to TriageResponse |
| 8 | `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/TriageResponse.cs` | **Modified** — added `PackSafeActionRecordItemDto` (7 fields), `PackSafeActionRecordSummaryDto` (5 fields), 15th param `PackSafeActionRecordSummary = null` |
| 9 | `docs/http/OpsCopilot.Api.http` | **Modified** — TOC entry for Section AT + 5 new test cases (AT1–AT5) |

---

## 3. Implementation Notes

### Cross-Module Bridge
`PackSafeActionRecorder` lives in Packs.Infrastructure but calls `ISafeActionProposalService`
(SafeActions module) via `IServiceScopeFactory.CreateScope()`, maintaining module isolation.

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
