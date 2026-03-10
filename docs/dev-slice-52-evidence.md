# Dev Slice 52 — Target Scope Allowlists for Evidence and Proposals

## Objective

Enforce tenant-approved Azure scope (subscription IDs and Log Analytics workspace IDs) during safe-action proposal and recording pipelines. Uses **strict "empty = none allowed"** semantics — if no allowlist is configured, all targets are denied.

## Files Changed

### Created

| File | Purpose |
|------|---------|
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Governance/TargetScopeDecision.cs` | Immutable record: `Allowed`, `ReasonCode`, `Message` with `Allow()` / `Deny()` factories |
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Governance/ITargetScopeEvaluator.cs` | Contract: `TargetScopeDecision Evaluate(string tenantId, string targetType, string targetValue)` |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/ConfigTargetScopeEvaluator.cs` | Config-driven evaluator reading `SafeActions:AllowedAzureSubscriptionIds` and `SafeActions:AllowedLogAnalyticsWorkspaceIds` |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/ConfigTargetScopeEvaluatorTests.cs` | 12 unit tests covering all evaluator branches |

### Modified

| File | Change |
|------|--------|
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionProposalResult.cs` | Added `ScopeAllowed`, `ScopeReasonCode`, `ScopeMessage` fields |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/PackSafeActionProposalDto.cs` | Added same 3 scope fields to DTO |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PacksInfrastructureExtensions.cs` | Registered `ConfigTargetScopeEvaluator` as singleton for `ITargetScopeEvaluator` |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/AgentRunEndpoints.cs` | Mapped scope fields in Select projection |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionProposer.cs` | Resolves evaluator from DI scope; `EnrichWithScope` for normal + error paths |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackSafeActionRecorder.cs` | Skip condition includes `ScopeAllowed == false` → `"scope_denied"` telemetry |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionProposerTests.cs` | Factory updated + 5 scope tests (32-36) |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackSafeActionRecorderTests.cs` | Factory updated + 1 scope test (17) |

## Reason Codes

| Code | Trigger |
|------|---------|
| `target_scope_missing_subscription` | No subscription IDs configured (empty = deny) |
| `target_scope_subscription_not_allowed` | Subscription ID not in allowlist |
| `target_scope_missing_workspace` | No workspace IDs configured (empty = deny) |
| `target_scope_workspace_not_allowed` | Workspace ID not in allowlist |
| `target_scope_unknown_target` | Unrecognised `targetType` |
| `target_scope_mixed_results` | Reserved for future multi-target scenarios |
| `scope_preview_failed` | Evaluator threw an exception (safe fallback: denied) |

## Design Decisions

- **Empty = none allowed (STRICT)**: Differs from executor allowlists which use "empty = allow all". Proposals are advisory, so strict-by-default prevents runaway proposals.
- **Additive DTO fields**: `ScopeAllowed : bool?`, `ScopeReasonCode : string?`, `ScopeMessage : string?` — nullable to preserve backward compatibility.
- **Same DI scope as governance**: Evaluator resolved from `IServiceScopeFactory` alongside `IGovernancePolicyEvaluator`.
- **Allowed path nulls**: When scope is allowed, `ScopeReasonCode` and `ScopeMessage` are set to `null` (not the decision's values) to keep payloads clean.
- **Error-path enrichment**: When pack definition fails to load, scope enrichment still runs with `ActionType="unknown"` and `PackName` from the error record.

## Build Summary

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Test Summary

| Assembly | Passed | Failed | Skipped |
|----------|--------|--------|---------|
| Governance.Tests | 31 | 0 | 0 |
| Connectors.Tests | 30 | 0 | 0 |
| Evaluation.Tests | 15 | 0 | 0 |
| AlertIngestion.Tests | 31 | 0 | 0 |
| Reporting.Tests | 27 | 0 | 0 |
| **Packs.Tests** | **274** | **0** | **0** |
| AgentRuns.Tests | 81 | 0 | 0 |
| Tenancy.Tests | 17 | 0 | 0 |
| SafeActions.Tests | 368 | 0 | 0 |
| Integration.Tests | 24 | 0 | 0 |
| Mcp.ContractTests | 8 | 0 | 0 |
| **Grand Total** | **906** | **0** | **0** |

## New Tests Added (18 total)

### ConfigTargetScopeEvaluatorTests (12)
1. `Evaluate_EmptySubscriptions_DenyMissing`
2. `Evaluate_SubscriptionInList_Allows`
3. `Evaluate_SubscriptionNotInList_DenyNotAllowed`
4. `Evaluate_EmptyWorkspaces_DenyMissing`
5. `Evaluate_WorkspaceInList_Allows`
6. `Evaluate_WorkspaceNotInList_DenyNotAllowed`
7. `Evaluate_UnknownTargetType_DenyUnknown`
8. `Evaluate_NoConfigKeys_BothDeny`
9. `Evaluate_MultipleSubscriptions_MatchesCorrectOne`
10. `Evaluate_MultipleWorkspaces_MatchesCorrectOne`
11. `Evaluate_SubscriptionCaseInsensitive`
12. `Evaluate_WorkspaceCaseInsensitive`

### PackSafeActionProposerTests (5)
32. `ProposeAsync_ScopeAllowed_SetsScopeFieldsCorrectly`
33. `ProposeAsync_ScopeDenied_PropagatesReasonCodeAndMessage`
34. `ProposeAsync_NoScopeEvaluator_ScopeFieldsRemainNull`
35. `ProposeAsync_ScopeEvaluatorThrows_FallsBackToFailed`
36. `ProposeAsync_ScopeOnErrorPath_WhenDefinitionReadFails`

### PackSafeActionRecorderTests (1)
17. `RecordAsync_SkipsScopeDeniedProposal`
