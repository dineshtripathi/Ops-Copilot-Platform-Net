# Slice 29 — Tenant-Aware Governance Resolution — Evidence

## Summary

Wires Governance policy evaluation to use Tenancy's SQL-backed per-tenant
config via a cross-module bridge contract (`ITenantConfigProvider`). Policies
now resolve per-tenant at runtime with a 3-tier priority cascade:
**SQL-backed tenant config → config-file TenantOverrides → GovernanceOptions.Defaults**.

**STRICT**: No new routes, no DTO changes, no schema changes, no execution
routing changes. This is internal configuration resolution plumbing only.

---

## AC Checklist

| # | Acceptance Criteria | Status | Evidence |
|---|---------------------|--------|----------|
| 1 | `ITenantConfigProvider` contract in BuildingBlocks.Contracts | ✅ | `ITenantConfigProvider.cs` + `TenantGovernanceConfig.cs` in `Contracts/Tenancy/` |
| 2 | Tenancy adapter implementing `ITenantConfigProvider` | ✅ | `TenantConfigProviderAdapter.cs` in `Tenancy.Infrastructure/Services/` |
| 3 | Tenant-aware governance resolver overlays tenant config on defaults | ✅ | `TenantAwareGovernanceOptionsResolver.cs` — 3-tier: SQL → TenantOverrides → Defaults |
| 4 | `DefaultToolAllowlistPolicy` uses tenant-aware options | ✅ | Injects `ITenantAwareGovernanceOptionsResolver`, calls `Resolve(tenantId)` |
| 5 | Calling sites pass tenantId through governance evaluation | ✅ | Policies already receive `tenantId` parameter — no signature changes needed |
| 6 | No new routes, no DTO changes, no execution routing changes | ✅ | Zero endpoint, DTO, or routing modifications |
| 7 | ≥ 12 new tests | ✅ | **16 tests** across 5 test files |
| 8 | `docs/http` updated with Section AE | ✅ | `OpsCopilot.Api.http` — Section AE (AE1–AE6) |
| 9 | Evidence document at `docs/dev-slice-29-evidence.md` | ✅ | This file |
| 10 | Build 0 warnings/errors, all tests pass | ✅ | **186 tests**, 0 failures |

---

## New Files (7)

| File | Layer |
|------|-------|
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Tenancy/ITenantConfigProvider.cs` | Contracts |
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Tenancy/TenantGovernanceConfig.cs` | Contracts |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Services/TenantConfigProviderAdapter.cs` | Infrastructure |
| `src/Modules/Governance/Application/OpsCopilot.Governance.Application/Configuration/ResolvedGovernanceOptions.cs` | Application |
| `src/Modules/Governance/Application/OpsCopilot.Governance.Application/Services/TenantAwareGovernanceOptionsResolver.cs` | Application |
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/TenantAwareGovernanceOptionsResolverTests.cs` | Tests |
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/DefaultToolAllowlistPolicyTests.cs` | Tests |
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/DefaultTokenBudgetPolicyTests.cs` | Tests |
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/DefaultSessionPolicyTests.cs` | Tests |
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/TenantConfigProviderAdapterTests.cs` | Tests |

## Modified Files (8)

| File | Change |
|------|--------|
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/OpsCopilot.Tenancy.Infrastructure.csproj` | Added BuildingBlocks.Contracts project reference |
| `src/Modules/Tenancy/Infrastructure/OpsCopilot.Tenancy.Infrastructure/Extensions/TenancyInfrastructureExtensions.cs` | Registered `ITenantConfigProvider → TenantConfigProviderAdapter` (Scoped) |
| `src/Modules/Governance/Application/OpsCopilot.Governance.Application/Policies/DefaultToolAllowlistPolicy.cs` | Uses `ITenantAwareGovernanceOptionsResolver` instead of `IOptions<GovernanceOptions>` |
| `src/Modules/Governance/Application/OpsCopilot.Governance.Application/Policies/DefaultTokenBudgetPolicy.cs` | Uses `ITenantAwareGovernanceOptionsResolver` instead of `IOptions<GovernanceOptions>` |
| `src/Modules/Governance/Application/OpsCopilot.Governance.Application/Policies/DefaultSessionPolicy.cs` | Uses `ITenantAwareGovernanceOptionsResolver` instead of `IOptions<GovernanceOptions>` |
| `src/Modules/Governance/Application/OpsCopilot.Governance.Application/GovernanceApplicationExtensions.cs` | Added resolver DI, changed 3 policies Singleton → Scoped |
| `src/Modules/Governance/Presentation/OpsCopilot.Governance.Presentation/Extensions/GovernancePresentationExtensions.cs` | Added tenant-aware resolution diagnostics log line |
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/OpsCopilot.Modules.Governance.Tests.csproj` | Added test packages + project references |
| `docs/http/OpsCopilot.Api.http` | Section AE — 6 requests for tenant-aware governance |

## Deleted Files (1)

| File | Reason |
|------|--------|
| `tests/Modules/Governance/OpsCopilot.Modules.Governance.Tests/Class1.cs` | Placeholder replaced by real test files |

---

## Test Results

```
Test summary: total: 186, failed: 0, succeeded: 186, skipped: 0, duration: 44.5s
Build succeeded in 58.0s
```

---

## Design Decisions

- **Cross-module bridge via BuildingBlocks.Contracts**: `ITenantConfigProvider` lives in the shared Contracts assembly, allowing Governance.Application to consume tenant config without referencing Tenancy directly — preserving modular-monolith dependency rules.
- **Optional provider injection**: `TenantAwareGovernanceOptionsResolver` accepts `ITenantConfigProvider? provider = null`, so Governance functions standalone when Tenancy is not registered (graceful degradation).
- **3-tier resolution cascade**: SQL-backed config (highest priority) → config-file `TenantOverrides` (middle) → `GovernanceOptions.Defaults` (fallback). Each tier fills in only for keys not set by a higher-priority tier.
- **Sync-over-async bridge**: `TenantConfigProviderAdapter` wraps async `ITenantConfigResolver` with `.GetAwaiter().GetResult()` because `ITenantConfigProvider` is synchronous. Acceptable for this in-process resolution; the async boundary stays at the adapter.
- **tenantId type bridge**: Governance uses `string` tenantId; Tenancy uses `Guid`. The adapter handles conversion via `Guid.TryParse`, returning null on invalid IDs.
- **Singleton → Scoped**: Three config-aware policies (`DefaultToolAllowlistPolicy`, `DefaultTokenBudgetPolicy`, `DefaultSessionPolicy`) changed from Singleton to Scoped to support per-request tenant resolution. `DegradedModePolicy` remains Singleton (config-independent).
- **Partial override merge**: When SQL provides partial config (e.g., `AllowedTools` but not `TokenBudget`), the resolver fills missing fields from config-file overrides, then from defaults — no "all or nothing" override behavior.
