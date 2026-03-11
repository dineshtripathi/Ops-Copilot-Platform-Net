# Dev Slice 47 — Governance Preview Semantics + Evidence Reconciliation (STRICT)

**Date:** 2026-03-06
**Commit:** efeb0b9745039d31b756fa6bf5167b81404d6d16

---

## 1. Objective

Fix Slice 46 inconsistencies:
1. Evidence doc date was wrong (`2025-07-01` → `2025-07-06`).
2. Evidence doc file count was ambiguous — clarified "(8 files in git including this evidence doc)".
3. Normalize governance-preview semantics: when policy **allows**, return `GovernanceReasonCode = null` and `GovernanceMessage = null` instead of synthetic `"ALLOWED"` / `"Policy check passed."`.

## 2. Frozen Semantics

| Rule | Condition | GovernanceAllowed | GovernanceReasonCode | GovernanceMessage |
|------|-----------|-------------------|----------------------|-------------------|
| A | Skip (no tenantId / Mode A) | `null` | `null` | `null` |
| B | Policy allows | `true` | `null` | `null` |
| C | Policy denies | `false` | `<policy reason>` | `<policy message>` |
| D | Policy call throws | `false` | `"governance_preview_failed"` | `"Governance preview could not be computed."` |

## 3. Files Changed

| # | File | Change |
|---|------|--------|
| 1 | `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionProposer.cs` | `EnrichWithGovernance`: allowed branch now returns `null` for ReasonCode and Message via ternary |
| 2 | `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionProposerTests.cs` | Tests 21, 27: `Assert.Equal("ALLOWED", ...)` → `Assert.Null(...)` |
| 3 | `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionProposerIntegrationTests.cs` | Tests 12, 16, 17: `Assert.Equal("ALLOWED", ...)` → `Assert.Null(...)` |
| 4 | `docs/dev-slice-46-evidence.md` | Date fix, file-count clarification, semantics update, test-21 description update |

**Totals:** 4 files changed (5 including this evidence doc).

## 4. Build Gate

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## 5. Test Gate

| Assembly | Passed | Failed | Skipped |
|----------|--------|--------|---------|
| OpsCopilot.Modules.Governance.Tests | 31 | 0 | 0 |
| OpsCopilot.Modules.Connectors.Tests | 30 | 0 | 0 |
| OpsCopilot.Modules.Evaluation.Tests | 15 | 0 | 0 |
| OpsCopilot.Modules.AlertIngestion.Tests | 31 | 0 | 0 |
| OpsCopilot.Modules.Reporting.Tests | 27 | 0 | 0 |
| OpsCopilot.Modules.Packs.Tests | 208 | 0 | 0 |
| OpsCopilot.Modules.AgentRuns.Tests | 81 | 0 | 0 |
| OpsCopilot.Modules.Tenancy.Tests | 17 | 0 | 0 |
| OpsCopilot.Modules.SafeActions.Tests | 368 | 0 | 0 |
| OpsCopilot.Integration.Tests | 24 | 0 | 0 |
| OpsCopilot.Mcp.ContractTests | 8 | 0 | 0 |
| **Grand Total** | **840** | **0** | **0** |

## 6. Acceptance Criteria

- [x] `GovernanceAllowed = true` ⇒ `GovernanceReasonCode = null`, `GovernanceMessage = null`
- [x] `GovernanceAllowed = false` ⇒ reason code and message preserved from policy / fallback
- [x] Skip path unchanged (all three fields `null`)
- [x] Slice 46 evidence doc date corrected
- [x] Slice 46 evidence doc file count clarified
- [x] Build: 0W / 0E
- [x] Tests: 840 / 840 green
