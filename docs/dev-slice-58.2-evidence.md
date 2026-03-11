# Dev Slice 58.2 — Evidence: Real Runbook ACL Policy Enforcement

## Objective
Replace `PermissiveRunbookAclFilter` with a real deterministic ACL policy so unauthorized runbook
results can never surface as citations or guidance.

---

## Pre-Flight Checks

| ID | Check | Finding | Status |
|----|-------|---------|--------|
| PF-1 | ACL seam location and shape | Synchronous `Filter(hits, caller)` in `IRunbookAclFilter`, called at `TriageOrchestrator` line 331 post-`rbResponse.Ok`, pre-citation-mapping | ✅ Pass |
| PF-2 | `RunbookSearchHit` ACL metadata | `AllowedGroups?` and `AllowedRoles?` already present as nullable optional params (added in Slice 58.1) | ✅ Pass |
| PF-3 | Caller context availability | `RunbookCallerContext.TenantOnly(tenantId)` used at call site — `Groups = []`, `Roles = []` — empty lists, not null | ✅ Pass |
| PF-4 | ACL ownership per DEPENDENCY_RULES | `Application/Acl/` is correct layer per `docs/pdd/DEPENDENCY_RULES.md` — Application owns policies/orchestration; pure in-memory logic requires no infrastructure dependencies | ✅ Pass |
| PF-5 | Empty-after-ACL semantics | `foreach (var hit in authorized)` — empty list produces zero citations. Pre-existing test `Orchestrator_WhenFilterReturnsEmpty_RunbookCitationsIsEmpty` validates this | ✅ Pass |
| PF-6 | Governance bypass verification | `docs/governance.md` confirms governance controls tool invocation (allowlists, token budgets, session TTLs); ACL filter operates on results after invocation — completely orthogonal, no bypass path exists | ✅ Pass |

No blocking gaps found. Implementation proceeded immediately after all 6 checks passed.

---

## Implementation Decisions

### Policy Design
The `TenantGroupRoleRunbookAclFilter` implements OR semantics: a caller is authorized if **any**
of the following conditions hold (evaluated in order, first match wins):

1. **Open runbook** — `hit.AllowedGroups is null AND hit.AllowedRoles is null` → allow
2. **Group match** — `hit.AllowedGroups` is non-null AND `caller.Groups` contains at least one
   matching group (StringComparer.OrdinalIgnoreCase) → allow
3. **Role match** — `hit.AllowedRoles` is non-null AND `caller.Roles` contains at least one
   matching role (StringComparer.OrdinalIgnoreCase) → allow
4. **Deny** — no condition matched

**Edge cases handled:**
- `AllowedGroups = []` (non-null empty list) = restricted to nobody → deny
- `AllowedRoles = []` (non-null empty list) = restricted to nobody → deny
- Group/role comparisons are case-insensitive (OrdinalIgnoreCase)

### Allocation optimization
The filter uses a lazy-allocated `List<RunbookSearchHit>` result to avoid heap allocation when all
hits are authorized or no hits exist. Returns the original list reference unchanged when empty.

### Files Changed
| File | Change |
|------|--------|
| `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Acl/TenantGroupRoleRunbookAclFilter.cs` | **Created** — real deterministic ACL policy |
| `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Extensions/AgentRunsApplicationExtensions.cs` | `PermissiveRunbookAclFilter` → `TenantGroupRoleRunbookAclFilter` in DI |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/RunbookAclFilterTests.cs` | +13 unit tests for `TenantGroupRoleRunbookAclFilter` |
| `PermissiveRunbookAclFilter.cs` | **Kept** — still available for direct test construction; not registered in DI |

### No Changes Needed
- `TriageOrchestrator.cs` — ACL call site already correct (Slice 58.1)
- `IRunbookAclFilter.cs` — Interface already correct (Slice 58.1)
- `RunbookSearchHit` — ACL metadata fields already present (Slice 58.1)
- `RunbookCitationIntegrationTests.cs` — Uses 4-arg constructor (no ACL fields); open runbooks pass through the new filter correctly
- `TriageOrchestratorTests.cs` — Same pattern; no changes needed
- `KqlGovernedEvidenceIntegrationTests.cs` — Same pattern; no changes needed

---

## Test Coverage Summary

### Pre-existing tests (untouched, 5 tests)
| Test | What it validates |
|------|-------------------|
| `PermissiveFilter_ReturnsAllHits_Unchanged` | Permissive filter passes all hits |
| `PermissiveFilter_EmptyList_ReturnsEmpty` | Permissive filter handles empty input |
| `Orchestrator_AclFilter_IsCalledWithCorrectContext` | `TriageOrchestrator` calls filter with correct `TenantOnly` context |
| `Orchestrator_WhenFilterReturnsEmpty_RunbookCitationsIsEmpty` | Empty filter output → no citations |
| `Orchestrator_WhenFilterReturnsSubset_OnlyAuthorizedHitsBecomeCitations` | Filter subset → only authorized hits become citations |

### New tests for `TenantGroupRoleRunbookAclFilter` (13 tests)
| Test | What it validates |
|------|-------------------|
| `TenantGroupRoleFilter_NullGroupsNullRoles_HitIsAllowed` | Open runbook (null/null) is always allowed |
| `TenantGroupRoleFilter_GroupsRestricted_CallerHasNoGroups_IsDenied` | Caller with no groups denied group-restricted hit |
| `TenantGroupRoleFilter_RolesRestricted_CallerHasNoRoles_IsDenied` | Caller with no roles denied role-restricted hit |
| `TenantGroupRoleFilter_GroupsRestricted_CallerGroupMatches_IsAllowed` | Matching caller group grants access |
| `TenantGroupRoleFilter_RolesRestricted_CallerRoleMatches_IsAllowed` | Matching caller role grants access |
| `TenantGroupRoleFilter_GroupsRestricted_CallerGroupNoMatch_IsDenied` | Non-matching groups → deny |
| `TenantGroupRoleFilter_MixedHits_ReturnsOnlyAuthorized` | Mixed list returns exactly the authorized subset |
| `TenantGroupRoleFilter_EmptyInput_ReturnsEmpty` | Empty hit list returns empty (no allocation) |
| `TenantGroupRoleFilter_AllDenied_ReturnsEmpty` | All restricted hits → empty output |
| `TenantGroupRoleFilter_MultipleGroups_FirstMatches_IsAllowed` | Multiple allowed groups — OR logic (first match wins) |
| `TenantGroupRoleFilter_BothGroupsAndRoles_OnlyRoleMatches_IsAllowed` | Groups+Roles present — role match alone is sufficient (OR across dimensions) |
| `TenantGroupRoleFilter_BothGroupsAndRoles_OnlyGroupMatches_IsAllowed` | Groups+Roles present — group match alone is sufficient (OR across dimensions) |
| `TenantGroupRoleFilter_EmptyAllowedGroupsList_NonNullEmptyList_IsDenied` | Non-null but empty `AllowedGroups` = restricted to nobody → deny |
| `TenantGroupRoleFilter_GroupMatch_IsCaseInsensitive` | OrdinalIgnoreCase comparison — "SRE" vs "sre" → allowed |

**Total new tests: 13** (plus 1 additional case-insensitivity test = 14 new, 19 total in file)

---

## Build and Test Gates

```
dotnet build .\OpsCopilot.sln --configuration Release -warnaserror
# Result: Build succeeded. 0 Warning(s). 0 Error(s).

dotnet test .\OpsCopilot.sln --no-build --configuration Release
# Result: All assemblies Passed! 0 failures.
#         OpsCopilot.Modules.AgentRuns.Tests: 122 passed (was 108 — +14 new tests)
```

---

## Acceptance Criteria

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | `TenantGroupRoleRunbookAclFilter` exists in `Application/Acl/` | ✅ |
| AC-2 | DI registration uses `TenantGroupRoleRunbookAclFilter` | ✅ |
| AC-3 | Open runbooks (null/null) pass through unconditionally | ✅ tested |
| AC-4 | Group-restricted hits denied when caller has no matching group | ✅ tested |
| AC-5 | Role-restricted hits denied when caller has no matching role | ✅ tested |
| AC-6 | Group match grants access (OR semantics) | ✅ tested |
| AC-7 | Role match grants access (OR semantics) | ✅ tested |
| AC-8 | Both AllowedGroups and AllowedRoles present — OR across dimensions | ✅ tested |
| AC-9 | Non-null empty `AllowedGroups` = restricted to nobody | ✅ tested |
| AC-10 | Case-insensitive group/role comparison | ✅ tested |
| AC-11 | `PermissiveRunbookAclFilter` retained (not deleted) | ✅ |
| AC-12 | No changes to `TriageOrchestrator`, `IRunbookAclFilter`, or `RunbookSearchHit` | ✅ |
| AC-13 | 0 build warnings/errors | ✅ Build succeeded. 0 Warning(s). 0 Error(s). |
| AC-14 | All existing 966 tests still pass | ✅ 122 passed in AgentRuns.Tests (+14 new), 0 failures across all assemblies |
