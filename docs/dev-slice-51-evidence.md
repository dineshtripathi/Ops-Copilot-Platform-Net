# Dev Slice 51 — Pack SafeAction Definition Validation + Human-Readable Preview

| Field | Value |
|-------|-------|
| **Status** | ✅ Complete |
| **Branch** | `main` |
| **Date** | 2025-07-25 |

---

## 1 — Objective

Add **additive-only** per-action definition validation and a deterministic **Operator Card** preview to the Pack SafeAction proposal pipeline.

- **Frozen minimal-schema validator** — static class with 12 error codes; no JSON-schema library dependency; pure `System.Text.Json` parsing.
- **Deterministic Operator Card** — human-readable text preview generated from the action definition.
- **3 new nullable fields** on the contract and DTO: `DefinitionValidationErrorCode`, `DefinitionValidationMessage`, `OperatorPreview`.
- **No new routes**, no DB changes, no new NuGet packages.
- **Recommend-only posture preserved** — invalid definitions set `IsExecutableNow = false` with `ExecutionBlockedReason = "invalid_definition"`.

---

## 2 — Files Changed

### Contracts — BuildingBlocks

| File | Status | Notes |
|------|--------|-------|
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/PackSafeActionProposalResult.cs` | Modified | 3 new nullable fields (`DefinitionValidationErrorCode`, `DefinitionValidationMessage`, `OperatorPreview`) added to record, with XML doc comments |

### Packs Module — Infrastructure

| File | Status | Notes |
|------|--------|-------|
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionDefinitionValidator.cs` | **New** | 197-line static class — 12 error codes, Operator Card generator |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionProposer.cs` | Modified | Wired validator into `BuildProposalItemAsync`; added `try-catch(JsonException)` bug fix for `JsonDocument.Parse` |

### AgentRuns Module — Presentation

| File | Status | Notes |
|------|--------|-------|
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/PackSafeActionProposalDto.cs` | Modified | 3 new nullable DTO fields (camelCase serialisation) |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Endpoints/AgentRunEndpoints.cs` | Modified | Pass-through mapping for 3 new fields |

### Tests

| File | Status | Notes |
|------|--------|-------|
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionDefinitionValidatorTests.cs` | **New** | 23 test methods (22 Fact + 1 Theory×3 = 25 runtime) |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionProposerTests.cs` | Modified | 31 test methods (29 Fact + 2 Theory) — added validation + Operator Card assertions |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionProposerIntegrationTests.cs` | Modified | 24 tests total (17 existing updated + 7 new: Tests 18-24) |

### Documentation

| File | Status | Notes |
|------|--------|-------|
| `docs/http/OpsCopilot.Api.http` | Modified | TOC entry + Section AU (6 test cases AU1-AU6) |
| `docs/dev-slice-51-evidence.md` | **New** | This file |

**Totals: 2 new + 7 modified + 1 evidence doc = 10 files**

---

## 3 — Implementation Notes

### 3.1 Validator (`PackSafeActionDefinitionValidator`)

- **Static class** — no DI registration needed; called directly from the proposer.
- **12 error codes** (frozen, deterministic):
  `definition_null`, `parse_error`, `not_object`, `missing_display_name`, `missing_action_type`, `invalid_id_format`, `id_mismatch`, `title_too_long`, `invalid_requires_mode`, `invalid_supports_rollback`, `invalid_parameters`, `invalid_defaults`.
- Returns `(bool IsValid, string? ErrorCode, string? ErrorMessage)` tuple.
- All string comparisons use `StringComparison.OrdinalIgnoreCase` where appropriate.

### 3.2 Operator Card

Deterministic human-readable format:

```
== Operator Card ==
Action : {displayName}
Type   : {actionType}
Params : {sorted comma-separated keys} | (none) | (parse error)
Valid  : yes | no — {errorCode}: {errorMessage}
```

- Params are sorted lexicographically for deterministic output.
- Card is always generated (even for invalid definitions, with best-effort data).

### 3.3 Bug Fix

`PackSafeActionProposer.BuildProposalItemAsync` — `JsonDocument.Parse(rawJson)` could throw `JsonException` for malformed JSON files. Fixed with `try-catch(JsonException)` that leaves fields at defaults so the validator catches as `parse_error`.

### 3.4 Mode & Feature Gates

- Proposals still require deployment mode ≥ B AND `Packs:SafeActionsEnabled=true`.
- Invalid definitions set `IsExecutableNow = false` and `ExecutionBlockedReason = "invalid_definition"`.
- Governance enrichment runs independently of validation — both field sets are populated.

### 3.5 Recommend-Only Posture

Mode A remains deterministic and offline. Validation is purely informational; it does not enable or block execution beyond the existing mode/feature gate logic.

---

## 4 — New Tests

### 4.1 PackSafeActionDefinitionValidatorTests (NEW — 23 methods / 25 runtime)

| # | Test | Type |
|---|------|------|
| 1 | `Validate_NullRawJson_ReturnsDefinitionNull` | Fact |
| 2 | `Validate_EmptyRawJson_ReturnsParseError` | Fact |
| 3 | `Validate_InvalidJson_ReturnsParseError` | Fact |
| 4 | `Validate_JsonArray_ReturnsNotObject` | Fact |
| 5 | `Validate_MissingDisplayName_ReturnsMissingDisplayName` | Fact |
| 6 | `Validate_EmptyDisplayName_ReturnsMissingDisplayName` | Fact |
| 7 | `Validate_MissingActionType_ReturnsMissingActionType` | Fact |
| 8 | `Validate_EmptyActionType_ReturnsMissingActionType` | Fact |
| 9 | `Validate_InvalidIdFormat_ReturnsInvalidIdFormat` | Fact |
| 10 | `Validate_IdMismatch_ReturnsIdMismatch` | Fact |
| 11 | `Validate_TitleTooLong_ReturnsTitleTooLong` | Fact |
| 12 | `Validate_InvalidRequiresMode_ReturnsInvalidRequiresMode` | Fact |
| 13 | `Validate_ValidRequiresMode_ReturnsIsValidTrue` | Theory×3 (A, B, C) |
| 14 | `Validate_InvalidSupportsRollback_ReturnsInvalidSupportsRollback` | Fact |
| 15 | `Validate_InvalidParameters_ReturnsInvalidParameters` | Fact |
| 16 | `Validate_InvalidDefaults_ReturnsInvalidDefaults` | Fact |
| 17 | `Validate_MinimalValidDefinition_ReturnsIsValid` | Fact |
| 18 | `Validate_FullValidDefinition_ReturnsIsValid` | Fact |
| 19 | `GenerateOperatorCard_ValidNoParams_ContainsNoneParamLine` | Fact |
| 20 | `GenerateOperatorCard_ValidWithParams_ContainsSortedParams` | Fact |
| 21 | `GenerateOperatorCard_Invalid_ContainsNoWithErrorCode` | Fact |
| 22 | `GenerateOperatorCard_ParseError_ContainsParseErrorInParams` | Fact |
| 23 | `GenerateOperatorCard_ContainsAllFourLines` | Fact |

### 4.2 New Integration Tests (Tests 18-24)

| # | Test | Scenario |
|---|------|----------|
| 18 | `FullPipeline_InvalidJsonDefinition_ReturnsParseError` | Malformed JSON → `parse_error` + blocked |
| 19 | `FullPipeline_MissingDisplayName_ReturnsMissingDisplayNameError` | Missing `displayName` → `missing_display_name` |
| 20 | `FullPipeline_MissingActionType_ReturnsMissingActionTypeError` | Missing `actionType` → `missing_action_type` |
| 21 | `FullPipeline_IdMismatch_ReturnsIdMismatchError` | Explicit `id` ≠ manifest `actionId` → `id_mismatch` |
| 22 | `FullPipeline_ValidDefinition_OperatorPreviewContainsExpectedLines` | Valid def → full Operator Card verified |
| 23 | `FullPipeline_MixedValidInvalidDefinitions_ReturnsCorrectPerActionValidation` | 2 actions, 1 valid + 1 invalid → independent results |
| 24 | `FullPipeline_GovernanceDeniedPlusInvalidDefinition_BothFieldSetsCorrect` | Governance denied + invalid def → both field sets populated |

---

## 5 — Gates

### Build

```
dotnet build OpsCopilot.sln -warnaserror   → 0 warnings, 0 errors
```

### Tests

```
dotnet test OpsCopilot.sln --no-build --verbosity minimal → 812 passed, 0 failed
```

| Assembly | Passed |
|----------|--------|
| OpsCopilot.Modules.Packs.Tests | 256 |
| OpsCopilot.Modules.SafeActions.Tests | 368 |
| OpsCopilot.Modules.AgentRuns.Tests | 81 |
| OpsCopilot.Modules.AlertIngestion.Tests | 31 |
| OpsCopilot.Modules.Reporting.Tests | 27 |
| OpsCopilot.Modules.Tenancy.Tests | 17 |
| OpsCopilot.IntegrationTests | 24 |
| OpsCopilot.McpContractTests | 8 |
| **Total** | **812** |
