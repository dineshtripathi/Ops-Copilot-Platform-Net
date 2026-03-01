# Slice 33 — Evidence: SafeActions Policy – Replace Allow-All Stub with Tenant-Aware Governance

## Acceptance Criteria Checklist

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | `GovernanceBackedSafeActionPolicy` implements `ISafeActionPolicy` and delegates to `IGovernancePolicyClient.EvaluateToolAllowlist` | ✅ |
| AC-2 | Propose is denied when governance tool allowlist denies the action for the tenant | ✅ |
| AC-3 | HTTP 400 response contains `reasonCode = "governance_tool_denied"` and message includes `policyReason=` | ✅ |
| AC-4 | Propose returns 201 when governance allows the action | ✅ |
| AC-5 | Tenant isolation — different tenants receive different policy decisions | ✅ |
| AC-6 | DI registration replaced: `DefaultSafeActionPolicy` → `GovernanceBackedSafeActionPolicy` (Scoped) | ✅ |
| AC-7 | ≥ 12 new tests, all green | ✅ |
| AC-8 | `.http` Section AJ appended (4 requests: AJ1–AJ4) | ✅ |
| AC-9 | Evidence document produced | ✅ |

## Files Changed / Created

| File | Action | Purpose |
|------|--------|---------|
| `src/Modules/SafeActions/Infrastructure/.../Policies/GovernanceBackedSafeActionPolicy.cs` | **Created** | Tenant-aware policy that delegates to `IGovernancePolicyClient.EvaluateToolAllowlist`; returns frozen code `governance_tool_denied` with `policyReason=` message on deny |
| `src/Modules/SafeActions/Infrastructure/.../Extensions/SafeActionsInfrastructureExtensions.cs` | **Modified** | Replaced `DefaultSafeActionPolicy` (Singleton) with `GovernanceBackedSafeActionPolicy` (Scoped) |
| `tests/.../OpsCopilot.Modules.SafeActions.Tests/GovernanceBackedSafeActionPolicyTests.cs` | **Created** | 9 unit tests for policy logic |
| `tests/.../OpsCopilot.Modules.SafeActions.Tests/SafeActionGovernancePolicyEndpointTests.cs` | **Created** | 4 endpoint tests for propose HTTP layer |
| `docs/http/OpsCopilot.Api.http` | **Modified** | Section AJ (AJ1–AJ4) appended |
| `docs/dev-slice-33-evidence.md` | **Created** | This evidence document |

## Test Results

**New tests in this slice: 13** (9 unit + 4 endpoint)

```
Total tests: 618
Passed:      618
Failed:        0
```

### Per-assembly breakdown (post-slice)

| Assembly | Count |
|----------|-------|
| `OpsCopilot.Modules.Governance.Tests` | 31 |
| `OpsCopilot.Modules.Connectors.Tests` | 30 |
| `OpsCopilot.Modules.AgentRuns.Tests` | 69 |
| `OpsCopilot.Modules.AlertIngestion.Tests` | 31 |
| `OpsCopilot.Modules.Evaluation.Tests` | 15 |
| `OpsCopilot.Modules.Reporting.Tests` | 27 |
| `OpsCopilot.Modules.Tenancy.Tests` | 17 |
| `OpsCopilot.Modules.SafeActions.Tests` | 368 (+13 new) |
| `OpsCopilot.Integration.Tests` | 24 |
| `OpsCopilot.Mcp.ContractTests` | 8 |
| **Total** | **618** |

## Design Notes

- **GovernanceBackedSafeActionPolicy** is the Gate-2 policy in `SafeActionOrchestrator.ProposeAsync`. It fires BEFORE the direct `IGovernancePolicyClient.EvaluateToolAllowlist` gate (Gate 3), providing a single ISafeActionPolicy abstraction that encapsulates governance checks.
- **Frozen reason code** `"governance_tool_denied"` is shared between Gate 2 (via `GovernanceBackedSafeActionPolicy`) and Gate 3 (via `GovernanceDenialMapper.ToolDenied`). Both carry `policyReason=` in the message for traceability.
- **Scoped lifetime** matches `IGovernancePolicyClient` registration, avoiding captive-dependency issues.
- `DefaultSafeActionPolicy` is intentionally NOT deleted — it remains available for scenarios (e.g., local dev) that bypass governance.
