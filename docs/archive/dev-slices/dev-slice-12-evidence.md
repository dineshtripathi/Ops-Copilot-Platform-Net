# Dev Slice 12 — SafeActions Azure Target Allowlists Hardening (STRICT)

## Objective

Add configuration-driven allowlists so only pre-approved Azure targets are
reachable by the two Azure action executors:

| Executor | Config Key | Scope |
|---|---|---|
| `azure_resource_get` | `SafeActions:AllowedAzureSubscriptionIds` | ARM subscription ID extracted from the resource ID |
| `azure_monitor_query` | `SafeActions:AllowedLogAnalyticsWorkspaceIds` | Log Analytics workspace ID from the action payload |

**Backward compatibility**: an empty allowlist (default) imposes no restriction.

## Design Decisions

| Decision | Rationale |
|---|---|
| Empty allowlist = allow all | Zero-config backward compatibility; existing deployments keep working |
| Case-insensitive `HashSet<string>` | Azure IDs are GUIDs — callers may use any casing |
| Validation order: format → allowlist → SDK | Reject cheaply before any network call |
| Error code `target_not_allowlisted` | Distinct from `invalid_resource_id` / `invalid_workspace_id` |
| Config read in constructor (not per-call) | Avoids re-parsing on every execution; restart to reload |

## Files Changed

### Production Code

| File | Change |
|---|---|
| [appsettings.Development.json](../src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json) | Added `AllowedAzureSubscriptionIds: []` and `AllowedLogAnalyticsWorkspaceIds: []` to `SafeActions` section |
| [AzureResourceGetActionExecutor.cs](../src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/Executors/AzureResourceGetActionExecutor.cs) | Added `_allowedSubscriptionIds` field, constructor config read, subscription allowlist check before SDK call |
| [AzureMonitorQueryActionExecutor.cs](../src/Modules/SafeActions/Infrastructure/OpsCopilot.SafeActions.Infrastructure/Executors/AzureMonitorQueryActionExecutor.cs) | Added `_allowedWorkspaceIds` field, constructor config read, workspace allowlist check after GUID validation |

### Test Code

| File | Tests Added | Total |
|---|---|---|
| [AzureResourceGetActionExecutorTests.cs](../tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/AzureResourceGetActionExecutorTests.cs) | 7 subscription allowlist tests | 26 |
| [AzureMonitorQueryActionExecutorTests.cs](../tests/Modules/SafeActions/OpsCopilot.Modules.SafeActions.Tests/AzureMonitorQueryActionExecutorTests.cs) | 7 workspace allowlist tests | 31 |

### Documentation

| File | Change |
|---|---|
| [OpsCopilot.Api.http](../docs/http/OpsCopilot.Api.http) | Section N (N1–N4): allowlist manual test entries |

## New Test Methods (14 total)

### Subscription Allowlist (AzureResourceGetActionExecutorTests)

1. `ExecuteAsync_EmptyAllowlist_AllowsAll` — empty config ⇒ no restriction
2. `ExecuteAsync_NoAllowlistConfig_AllowsAll` — missing config ⇒ no restriction
3. `ExecuteAsync_AllowlistedSubscription_Succeeds` — matching sub ⇒ success
4. `ExecuteAsync_NonAllowlistedSubscription_Returns_TargetNotAllowlisted` — mismatched sub ⇒ error
5. `ExecuteAsync_SubscriptionAllowlist_CaseInsensitive` — upper-case ID matches lower-case config
6. `ExecuteAsync_MultipleAllowedSubscriptions_AcceptsAny` — any match in set suffices
7. `ExecuteAsync_NonAllowlistedSubscription_DetailContainsSubscriptionId` — error detail includes the rejected ID

### Workspace Allowlist (AzureMonitorQueryActionExecutorTests)

1. `ExecuteAsync_EmptyWorkspaceAllowlist_AllowsAll` — empty config ⇒ no restriction
2. `ExecuteAsync_NoWorkspaceAllowlistConfig_AllowsAll` — missing config ⇒ no restriction
3. `ExecuteAsync_AllowlistedWorkspace_Succeeds` — matching workspace ⇒ success
4. `ExecuteAsync_NonAllowlistedWorkspace_Returns_TargetNotAllowlisted` — mismatched workspace ⇒ error
5. `ExecuteAsync_WorkspaceAllowlist_CaseInsensitive` — upper-case ID matches lower-case config
6. `ExecuteAsync_MultipleAllowedWorkspaces_AcceptsAny` — any match in set suffices
7. `ExecuteAsync_NonAllowlistedWorkspace_DetailContainsWorkspaceId` — error detail includes the rejected ID

## Test Results

```
Total tests: 269 (up from 255)
  AgentRuns:    53  ✅
  SafeActions: 184  ✅  (+14 new)
  Integration:  24  ✅
  MCP Contract:  8  ✅
Failures: 0
```

## Configuration Reference

```jsonc
// SafeActions section in appsettings
{
  "SafeActions": {
    // ... existing keys ...
    "AllowedAzureSubscriptionIds": [],        // empty = allow all
    "AllowedLogAnalyticsWorkspaceIds": []     // empty = allow all
  }
}
```

To restrict, populate with GUID strings:

```jsonc
{
  "SafeActions": {
    "AllowedAzureSubscriptionIds": [
      "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    ],
    "AllowedLogAnalyticsWorkspaceIds": [
      "11111111-2222-3333-4444-555555555555"
    ]
  }
}
```

## .http Section N Quick Reference

| Entry | Purpose | Expected |
|---|---|---|
| N1 | Allowlisted subscription GET | `200` success |
| N2 | Non-allowlisted subscription | `200` with `target_not_allowlisted` |
| N3 | Allowlisted workspace query | `200` success |
| N4 | Non-allowlisted workspace | `200` with `target_not_allowlisted` |

## Safety Controls

- **No Azure writes** — both executors are read-only GET / query
- **No secrets in logs or responses** — error detail shows only the rejected ID
- **Feature-gated** — `EnableAzureReadExecutions` / `EnableAzureMonitorReadExecutions` still required
- **Default-off** — empty allowlists impose no restriction (backward compatible)
- **Restart to reload** — allowlists read once in constructor

## Hard Constraints Maintained

- No schema changes to `ActionExecutionResult`
- No route / MCP / Worker changes
- No refactors outside slice scope
- All new behavior default-off
