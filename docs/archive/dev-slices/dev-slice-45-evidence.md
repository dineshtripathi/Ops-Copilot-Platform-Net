# Dev Slice 45 — Mode B "Recommend" Alignment for Pack SafeActions

## Plan

Mode B deployments must return **all** SafeAction proposals regardless of per-action
`requiresMode`. Actions whose `requiresMode` exceeds the current deployment mode are
included but marked non-executable (`IsExecutableNow = false`,
`ExecutionBlockedReason = "requires_higher_mode"`). This is strictly
recommend-only — no auto-approve, no auto-execute, no routing into the SafeActions
execution pipeline.

### Changes required

| Area | Description |
|------|-------------|
| Contract | Add `IsExecutableNow` (bool) and `ExecutionBlockedReason` (string?) to `PackSafeActionProposalItem` |
| DTO | Mirror the two new fields in `PackSafeActionProposalDto` |
| Proposer | Replace per-action `continue` with eligibility computation; always emit the action |
| Endpoint | Map new fields in the `.Select()` lambda |
| Validation | Relax Rule 9 from `requiresMode == "C"` to `requiresMode ∈ {A, B, C}` |
| Tests | Update existing + add new unit/integration tests |
| .http | Add Section AR (4 manual requests) |

---

## Files Changed

| Action | File | Summary |
|--------|------|---------|
| Modified | `src/BuildingBlocks/Contracts/…/PackSafeActionProposalResult.cs` | Added positional params `bool IsExecutableNow` and `string? ExecutionBlockedReason` to `PackSafeActionProposalItem` |
| Modified | `src/Modules/AgentRuns/Presentation/…/PackSafeActionProposalDto.cs` | Added positional params `bool IsExecutableNow` and `string? ExecutionBlockedReason` |
| Modified | `src/Modules/Packs/Infrastructure/…/PackSafeActionProposer.cs` | Replaced `continue` with `IsModeAtOrBelow` eligibility check; both happy/error paths pass `isExecutableNow`/`executionBlockedReason` |
| Modified | `src/Modules/AgentRuns/Presentation/…/AgentRunEndpoints.cs` | DTO `.Select()` lambda now maps `p.IsExecutableNow`, `p.ExecutionBlockedReason` |
| Modified | `src/Modules/Packs/Infrastructure/…/FileSystemPackLoader.cs` | Rule 9 relaxed from `== "C"` to `ValidModes.Contains(…)` (accepts A/B/C) |
| Modified | `tests/…/PackSafeActionProposerTests.cs` | Updated 8 tests (3,7,8,9,10,11,12,18); added 2 new tests (19,20). 20 total. |
| Modified | `tests/…/PackSafeActionProposerIntegrationTests.cs` | Updated 6 tests (1,4,5,7,8,10); added 1 new test (11). 11 total. |
| Modified | `tests/…/FileSystemPackLoaderValidationTests.cs` | Replaced `Validate_SafeActionRequiresModeNotC_ReturnsError` with `[Theory]` pair for invalid (Z/D/"") and valid (A/B/C) modes. Net +5 test cases. |
| Modified | `docs/http/OpsCopilot.Api.http` | TOC updated; AQ2/AQ6 comments refreshed; Section AR appended (4 requests) |
| Created  | `docs/dev-slice-45-evidence.md` | This file |

---

## Implementation Notes

### 1. Contract expansion (additive-only)

`PackSafeActionProposalItem` gained two trailing positional parameters:

```
param 9:  bool   IsExecutableNow        – true when IsModeAtOrBelow(action.RequiresMode, deploymentMode)
param 10: string? ExecutionBlockedReason – null when executable, "requires_higher_mode" otherwise
```

### 2. Proposer eligibility logic

The `DiscoverPackActionsAsync` inner loop previously skipped actions above the
deployment mode via `continue`. The replacement computes eligibility inline:

```csharp
bool isExecutableNow = IsModeAtOrBelow(action.RequiresMode, deploymentMode);
string? executionBlockedReason = isExecutableNow ? null : "requires_higher_mode";
```

Both values are threaded through `BuildProposalItemAsync` to the final result record.

### 3. Rule 9 relaxation

The original Rule 9 (`safeActions[].requiresMode must be "C"`) blocked packs
with mixed-mode safe actions from loading. Slice 45 needs packs to declare
actions at A, B, or C. Rule 9 now validates against the existing `ValidModes`
HashSet (`{ "A", "B", "C" }`), aligning with Rule 8 (evidence collectors).

### 4. Deterministic, offline

All changes are config-driven (SafeActionsEnabled flag) and deterministic.
No external service calls, no AI invocations. Mode A remains unaffected.

---

## Test List

### Unit tests — `PackSafeActionProposerTests` (20 tests)

| # | Test | Status |
|---|------|--------|
| 1 | `ProposeAsync_ModeADeployment_ReturnsEmptyProposals` | Pass |
| 2 | `ProposeAsync_SafeActionsDisabled_ReturnsEmptyProposals` | Pass |
| 3 | `ProposeAsync_ModeBDeployment_ReturnsProposals` | Pass (updated) |
| 4 | `ProposeAsync_ModeCDeployment_ReturnsProposals` | Pass |
| 5 | `ProposeAsync_NoPacks_ReturnsEmptyProposals` | Pass |
| 6 | `ProposeAsync_PackBelowMinimumMode_ReturnsEmptyProposals` | Pass |
| 7 | `ProposeAsync_ActionAboveDeploymentMode_ReturnsNotExecutableProposal` | Pass (rewritten) |
| 8 | `ProposeAsync_ModeCDeployment_IncludesAllActions` | Pass (updated) |
| 9 | `ProposeAsync_DefinitionFileContent_IncludedInResult` | Pass (updated) |
| 10 | `ProposeAsync_DefinitionReadError_CapturedInErrors` | Pass (updated) |
| 11 | `ProposeAsync_MultipleEligiblePacks_ReturnsCombinedProposals` | Pass (updated) |
| 12 | `ProposeAsync_EmitsTelemetryWithPackAndActionCounts` | Pass (updated) |
| 13 | `IsModeAtOrBelow_NullOrEmpty_ReturnsFalse` | Pass |
| 14 | `IsModeAtOrBelow_InvalidMode_ReturnsFalse` | Pass |
| 15 | `IsModeAtOrBelow_EqualMode_ReturnsTrue` | Pass |
| 16 | `IsModeAtOrBelow_LowerMode_ReturnsTrue` | Pass |
| 17 | `IsModeAtOrBelow_HigherMode_ReturnsFalse` | Pass |
| 18 | `ProposeAsync_DefinitionFileNull_ProposalHasNullDefinition` | Pass (updated) |
| 19 | `ProposeAsync_ModeBMixedActions_SetsCorrectEligibility` | Pass (new) |
| 20 | `ProposeAsync_ModeCMixedActions_AllExecutable` | Pass (new) |

### Integration tests — `PackSafeActionProposerIntegrationTests` (11 tests)

| # | Test | Status |
|---|------|--------|
| 1 | `FullPipeline_ModeCPackWithDefinition_ReturnsParsedProposal` | Pass (updated) |
| 2 | `FullPipeline_InvalidPackSkipped_EmptyResult` | Pass |
| 3 | `FullPipeline_SafeActionsDisabled_EmptyResult` | Pass |
| 4 | `FullPipeline_ModeCDeployment_IncludesAllActions` | Pass (updated) |
| 5 | `FullPipeline_ModeBDeployment_ReturnsNotExecutableProposals` | Pass (updated) |
| 6 | `FullPipeline_ModeADeployment_ReturnsEmpty` | Pass |
| 7 | `FullPipeline_MissingDefinitionFile_CapturesError` | Pass (updated) |
| 8 | `FullPipeline_PackBelowMinimumMode_Excluded` | Pass (updated) |
| 9 | `FullPipeline_MultiplePacksCombined` | Pass |
| 10 | `FullPipeline_NoDefinitionFile_ProposalHasNullDefinition` | Pass (updated) |
| 11 | `FullPipeline_ModeBMixedActions_SetsCorrectEligibility` | Pass (new) |

### Validation tests — `FileSystemPackLoaderValidationTests` (updated)

| Test | Status |
|------|--------|
| `Validate_SafeActionRequiresModeInvalid_ReturnsError` [Theory: Z, D, ""] | Pass (new, replaces old) |
| `Validate_SafeActionRequiresModeValid_NoError` [Theory: A, B, C] | Pass (new) |

---

## Evidence

### Build gate

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Test gate

```
Passed! - Failed: 0, Passed:  31 - OpsCopilot.Modules.Governance.Tests.dll
Passed! - Failed: 0, Passed:  30 - OpsCopilot.Modules.Connectors.Tests.dll
Passed! - Failed: 0, Passed:  15 - OpsCopilot.Modules.Evaluation.Tests.dll
Passed! - Failed: 0, Passed:  31 - OpsCopilot.Modules.AlertIngestion.Tests.dll
Passed! - Failed: 0, Passed:  27 - OpsCopilot.Modules.Reporting.Tests.dll
Passed! - Failed: 0, Passed:  81 - OpsCopilot.Modules.AgentRuns.Tests.dll
Passed! - Failed: 0, Passed:  17 - OpsCopilot.Modules.Tenancy.Tests.dll
Passed! - Failed: 0, Passed: 191 - OpsCopilot.Modules.Packs.Tests.dll
Passed! - Failed: 0, Passed: 368 - OpsCopilot.Modules.SafeActions.Tests.dll
Passed! - Failed: 0, Passed:  24 - OpsCopilot.Integration.Tests.dll
Passed! - Failed: 0, Passed:   8 - OpsCopilot.Mcp.ContractTests.dll
────────────────────────────────────────────────
Grand total: 823 passed, 0 failed, 0 skipped
```

Delta from Slice 44: **815 → 823** (+8 net: 3 new tests + 5 from `[Fact]→[Theory]` expansion).
