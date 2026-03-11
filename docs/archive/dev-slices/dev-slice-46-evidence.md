# Dev Slice 46 — Governance Preview for Pack SafeAction Proposals

**Status:** Complete  
**Branch:** main  
**Base commit:** c2ac990  
**Commit:** 10d1a66  
**Date:** 2026-03-06

---

## 1. Objective

Add governance preview enrichment to each `PackSafeActionProposalItem` returned by the Mode B/C safe-action proposer. Three new nullable fields indicate whether an action type is allowlisted by the tenant's `IToolAllowlistPolicy`. This is **recommendation-only** — no auto-approve, no auto-execute, no new routes, no schema changes.

---

## 2. Files Changed

| # | File | Change |
|---|------|--------|
| 1 | `src/BuildingBlocks/Contracts/…/Packs/PackSafeActionProposalResult.cs` | Added 3 governance fields (`GovernanceAllowed`, `GovernanceReasonCode`, `GovernanceMessage`) with `= null` defaults + XML doc comments |
| 2 | `src/Modules/AgentRuns/Presentation/…/Contracts/PackSafeActionProposalDto.cs` | Added same 3 nullable fields with `= null` defaults |
| 3 | `src/Modules/Packs/Infrastructure/…/PackSafeActionProposer.cs` | Added `IServiceScopeFactory` DI; rewrote `DiscoverPackActionsAsync` to create per-call scope for `IToolAllowlistPolicy`; added `EnrichWithGovernance` static helper |
| 4 | `src/Modules/AgentRuns/Presentation/…/Endpoints/AgentRunEndpoints.cs` | Extended `.Select` mapping to include 3 governance fields |
| 5 | `tests/Modules/Packs/…/PackSafeActionProposerTests.cs` | Updated helpers + added 11 new unit tests (tests 21-31) |
| 6 | `tests/Modules/Packs/…/PackSafeActionProposerIntegrationTests.cs` | Updated `CreateProposer` + added `CreateScopeFactory` + added 6 new integration tests (tests 12-17) |
| 7 | `docs/http/OpsCopilot.Api.http` | Added TOC entry + Section AS with 4 manual test requests |

**Totals:** 7 code files changed, +745 / −10 lines (8 files in git including this evidence doc).

---

## 3. Implementation Notes

### DI Lifetime Resolution
`PackSafeActionProposer` is registered as **Singleton** but `IToolAllowlistPolicy` is **Scoped**. Resolved by injecting `IServiceScopeFactory` and creating a per-call scope inside `DiscoverPackActionsAsync`.

### EnrichWithGovernance
Static helper wraps `IToolAllowlistPolicy.CanUseTool(tenantId, actionType)` in try/catch:
- **Success → Allow:** `GovernanceAllowed = true`, `GovernanceReasonCode = null`, `GovernanceMessage = null`
- **Success → Deny:** `GovernanceAllowed = false`, `GovernanceReasonCode = <policy reason>`, `GovernanceMessage = <policy message>`
- **Exception:** `GovernanceAllowed = false`, `GovernanceReasonCode = "governance_preview_failed"`, `GovernanceMessage = "Governance preview could not be computed."`

> **Note:** Slice 47 later normalized semantics so Policy Allowed ⇒ `GovernanceReasonCode`/`GovernanceMessage` are `null` (boolean alone signals allow).

### Mode A Behavior
Mode A requests produce no proposals (unchanged behavior) → governance fields never populate.

### Missing Tenant
When `TenantId` is null/empty, governance enrichment is skipped → all three fields remain `null`.

---

## 4. New Tests

### Unit Tests (PackSafeActionProposerTests.cs) — 11 new

| # | Test Name | Scenario |
|---|-----------|----------|
| 21 | `ProposeAsync_GovernanceAllowed_WhenToolIsOnAllowlist` | Policy returns Allow → `GovernanceAllowed = true`, `GovernanceReasonCode = null`, `GovernanceMessage = null` |
| 22 | `ProposeAsync_GovernanceDenied_WhenToolNotOnAllowlist` | Policy returns Deny → `GovernanceAllowed = false`, `GovernanceReasonCode = "not_allowlisted"` |
| 23 | `ProposeAsync_GovernanceNull_WhenTenantIdMissing` | No tenant → all governance fields null |
| 24 | `ProposeAsync_GovernanceNull_WhenModeIsA` | Mode A → no proposals at all |
| 25 | `ProposeAsync_GovernanceDoesNotBlock_Proposal` | Governance deny doesn't suppress the proposal |
| 26 | `ProposeAsync_GovernanceFailed_WhenPolicyThrows` | Policy throws → `GovernanceReasonCode = "governance_preview_failed"` |
| 27 | `ProposeAsync_GovernanceAllowed_MultipleActions` | Two actions, both allowed |
| 28 | `ProposeAsync_GovernanceOnErrorPath_WhenDefinitionReadFails` | File read fails → error item gets governance enrichment with `ActionType = "unknown"` |
| 29 | `ProposeAsync_ModeA_NoGovernanceComputed` | Explicit Mode A guard test |
| 30 | `ProposeAsync_GovernanceDenied_WithCustomReasonCode` | Custom reason codes flow through |
| 31 | `ProposeAsync_GovernanceMessage_FlowsThrough` | Custom messages flow through |

### Integration Tests (PackSafeActionProposerIntegrationTests.cs) — 6 new

| # | Test Name | Scenario |
|---|-----------|----------|
| 12 | `ProposeAsync_GovernanceAllowed_Integration` | Real file pipeline + Allow policy → fields set |
| 13 | `ProposeAsync_GovernanceDenied_Integration` | Real file pipeline + Deny policy |
| 14 | `ProposeAsync_GovernanceNull_MissingTenant_Integration` | No tenant → null fields |
| 15 | `ProposeAsync_GovernanceFailed_PolicyThrows_Integration` | Policy throws → safe fallback |
| 16 | `ProposeAsync_GovernanceAllowed_MultipleActions_Integration` | Two actions w/ real files |
| 17 | `ProposeAsync_GovernanceAllowed_WithDefinitionLoaded_Integration` | Verify governance does not affect definition loading |

### .http Manual Tests (Section AS) — 4 requests

| # | Request | Purpose |
|---|---------|---------|
| AS1 | POST triage Mode B, valid tenant, allowed action | Verify `governanceAllowed: true` |
| AS2 | POST triage Mode B, valid tenant, denied action | Verify `governanceAllowed: false` |
| AS3 | POST triage Mode A | Verify no proposals / no governance |
| AS4 | POST triage Mode B, no tenant header | Verify governance fields null |

---

## 5. Acceptance Criteria

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | Additive fields with `= null` defaults; existing callers unaffected | ✅ |
| AC-2 | Proposer computes governance preview in Mode B/C | ✅ |
| AC-3 | Never throws; swallows exceptions → safe defaults | ✅ |
| AC-4 | Tenant-aware; missing tenant → null fields | ✅ |
| AC-5 | Mode A unchanged (no proposals) | ✅ |
| AC-6 | No new routes, no schema changes | ✅ |
| AC-7 | ≥ 10 new unit tests | ✅ (11) |
| AC-8 | ≥ 6 new integration tests | ✅ (6) |
| AC-9 | .http Section AS with 4 requests | ✅ |
| AC-10 | Build 0W/0E, all tests green | ✅ |

---

## 6. Build & Test Evidence

```
Build succeeded. 0 Warning(s) 0 Error(s)

Test summary: total: 840, failed: 0, succeeded: 840, skipped: 0
```

**Pre-slice baseline:** 823 tests  
**Post-slice total:** 840 tests (+17 new)
