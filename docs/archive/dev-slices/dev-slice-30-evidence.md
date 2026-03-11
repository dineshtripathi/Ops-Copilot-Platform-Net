# Slice 30 — Tenant-Aware Governance End-to-End Verification (STRICT) — Evidence

> **Corrections (post-commit reconciliation)**
>
> 1. **Test totals**: Originally reported 352 — actual `dotnet test` total is **572** (all passing).
> 2. **Section AF routes**: AF2/AF3 used non-existent `PUT /tenants/{id}/config/…` routes — corrected to `PUT /tenants/{id}/settings` with `{ key, value }` body. AF4 used `GET /tenants/{id}/config` — corrected to `GET /tenants/{id}/settings/resolved`.
> 3. **SafeActions wording**: Section renamed from “SafeActions Proof Tests” to “Contract Compatibility Proof — PolicyDecision” to clarify that no SafeActions runtime behavior was altered.

## Summary

Pure verification slice — no new production code. Proves that tenant config
stored in Tenancy SQL drives Governance policies at runtime via the cross-module
bridge (`ITenantConfigProvider` → `TenantConfigProviderAdapter`). Includes
cross-module integration tests exercising the full chain
(Mock SQL → Adapter → Resolver → Policy) and SafeActions proof tests
demonstrating the shared `PolicyDecision` contract enables SafeActions to
consume governance outcomes.

**STRICT**: No new routes, no schema changes, no migrations, no DTO changes.
Test-only and documentation additions.

---

## AC Checklist

| # | Acceptance Criteria | Status | Evidence |
|---|---------------------|--------|----------|
| 1 | Cross-module integration tests (≥ 10) | ✅ | **12 tests** in `TenantGovernanceEndToEndTests.cs` |
| 2 | Contract compatibility proof tests (≥ 2) | ✅ | **3 tests** in `SafeActionsGovernanceProofTests.cs` |
| 3 | `.http` Section AF (tenant-verify-30) with ≥ 6 requests | ✅ | `OpsCopilot.Api.http` — Section AF (AF1–AF6) |
| 4 | Evidence document at `docs/dev-slice-30-evidence.md` | ✅ | This file |
| 5 | Build 0 warnings/errors, all tests pass | ✅ | **572 tests**, 0 failures, 0 warnings |
| 6 | No new production code, routes, DTOs, or schema changes | ✅ | Only test files and docs added/modified |

---

## New Files (2)

| File | Layer |
|------|-------|
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/CrossModule/TenantGovernanceEndToEndTests.cs` | Tests |
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/CrossModule/SafeActionsGovernanceProofTests.cs` | Tests |

## Modified Files (1)

| File | Change |
|------|--------|
| `docs/http/OpsCopilot.Api.http` | TOC updated + Section AF appended (6 requests AF1–AF6) |

---

## Cross-Module Integration Tests (12)

All tests wire **Mock `ITenantConfigResolver` → real `TenantConfigProviderAdapter` → real `TenantAwareGovernanceOptionsResolver` → real Policy** and verify the full 3-tier resolution chain.

| # | Test Name | What It Proves |
|---|-----------|----------------|
| 1 | `FullChain_SqlAllowedTools_AllowlistPolicyEnforces` | SQL restricts tools → `CanUseTool` denies unlisted tool |
| 2 | `FullChain_SqlTokenBudget_BudgetPolicyReturnsCap` | SQL budget 2048 → policy returns MaxTokens = 2048 |
| 3 | `FullChain_SqlSessionTtl_SessionPolicyReturnsTtl` | SQL TTL 15 min → policy returns `TimeSpan.FromMinutes(15)` |
| 4 | `FullChain_SqlUnavailable_FallsBackToConfigOverride` | SQL throws → config-file TenantOverrides used |
| 5 | `FullChain_SqlUnavailableNoOverride_FallsToDefaults` | SQL throws + no override → `GovernanceOptions.Defaults` |
| 6 | `FullChain_TwoTenants_DifferentGovernance` | Tenant A and B get independent governance — isolation proof |
| 7 | `FullChain_InvalidTenantId_FallsBackToDefaults` | "not-a-guid" → defaults, SQL never called |
| 8 | `FullChain_SqlReturnsFullConfig_AllFieldsHonored` | Custom tools + 9999 budget + 120 min TTL all respected |
| 9 | `FullChain_SqlNullBudget_BudgetPolicyReturnsUnlimited` | Null budget → `MaxTokens = null` (unlimited) |
| 10 | `FullChain_SqlEmptyAllowlist_AllToolsAllowed` | Empty allowlist = open gate (`IsAllowed = true`) |
| 11 | `Adapter_DropsTriageEnabled_MapsRemainingFields` | 4-field `EffectiveTenantConfig` → 3-field `TenantGovernanceConfig` |
| 12 | `FullChain_SqlOverridesConfigFile_SqlWins` | SQL values trump config-file override values |

## Contract Compatibility Proof — PolicyDecision (3)

Prove the shared `PolicyDecision` contract from `BuildingBlocks.Contracts.Governance` is compatible with SafeActions' consumption pattern. Uses the governance resolution chain to produce `PolicyDecision` values whose shape matches `ISafeActionPolicy`'s return type. **These tests do NOT alter SafeActions runtime decisions** — `DefaultSafeActionPolicy` (always-allow stub) remains unchanged.

| # | Test Name | What It Proves |
|---|-----------|----------------|
| 1 | `SafeActionsScenario_RestrictedTenant_GovernanceDeniesUnlisted` | SQL allows only `kql_query` → `restart_vm` denied with `TOOL_DENIED` reason |
| 2 | `SafeActionsScenario_PermissiveTenant_GovernanceAllowsListed` | SQL allows `restart_vm` → allowed decision |
| 3 | `SafeActionsScenario_TwoTenants_DifferentDecisions` | Same tool, different tenants → one denied, one allowed |

---

## .http Section AF — Requests

| # | Request | Purpose |
|---|---------|---------|
| AF1 | `POST /tenants` | Create second tenant `contoso-governance-30` |
| AF2 | `PUT /tenants/{id}/settings` | Set `{ key: "Governance:AllowedTools", value: "[\"runbook_search\"]" }` |
| AF3 | `PUT /tenants/{id}/settings` | Set `{ key: "Governance:TokenBudget", value: "10000" }` |
| AF4 | `GET /tenants/{id}/settings/resolved` | Verify second tenant's resolved overrides |
| AF5 | Triage with second tenant | Second tenant's restricted governance applies |
| AF6 | Triage with first tenant (AE) | First tenant's governance isolation preserved |

---

## Test Results

```
Total tests:  572, failed: 0, succeeded: 572, skipped: 0
Build:        0 Warning(s) 0 Error(s)

Governance tests: 31 (12 cross-module + 3 contract-proof + 16 from Slice 29)
```

---

## Design Decisions

- **Test-only verification slice**: No production code was modified. All 15 new tests (12 integration + 3 proof) validate existing Slice 29 wiring without introducing new behavior.
- **Governance test project hosts all cross-module tests**: Tests live in `OpsCopilot.Modules.Governance.Tests/CrossModule/` because they verify governance policy outcomes. The test project already has project references to Tenancy.Infrastructure and Contracts (added in Slice 29).
- **Contract compatibility proof via shared `PolicyDecision`**: `DefaultSafeActionPolicy` (always-allow stub) was NOT modified. Proof tests demonstrate that the governance resolution chain produces `PolicyDecision` values (with `IsAllowed`, `ReasonCode`) whose shape is compatible with SafeActions' consumption pattern. The shared `PolicyDecision` record in `BuildingBlocks.Contracts.Governance` bridges both modules. No SafeActions runtime decisions were altered.
- **Full chain, no mocking of internal layers**: Tests mock only the outermost boundary (`ITenantConfigResolver`, representing the SQL data store) and use real implementations for adapter, resolver, and policies. This maximizes integration coverage.
- **Cross-tenant isolation pattern**: Multiple tests verify that configuring governance for Tenant A does not affect Tenant B's policy evaluation, proving per-request tenant isolation.
- **3-tier fallback demonstrated**: Tests cover all three tiers — SQL available (P1), SQL unavailable with config-file fallback (P2), and default fallback (P3) — proving the cascade works end-to-end.
- **Section AF complements AE**: Section AE (Slice 29) tests single-tenant governance config. Section AF creates a second tenant with different governance settings and verifies isolation between the two.
