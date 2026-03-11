using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.BuildingBlocks.Contracts.Rag;

namespace OpsCopilot.AgentRuns.Application.Acl;

/// <summary>
/// Deterministic ACL filter that enforces per-runbook Group and Role restrictions.
///
/// Allow rules (evaluated in order, first match wins):
///   1. Open runbook  — AllowedGroups is null AND AllowedRoles is null → allow.
///   2. Group match   — AllowedGroups is non-null AND caller.Groups contains at least one
///                      group from AllowedGroups (OrdinalIgnoreCase) → allow.
///   3. Role match    — AllowedRoles is non-null AND caller.Roles contains at least one
///                      role from AllowedRoles (OrdinalIgnoreCase) → allow.
///   4. Otherwise     → deny.
///
/// Edge cases:
///   • AllowedGroups = [] (non-null empty list) = restricted to nobody → deny.
///   • AllowedRoles  = [] (non-null empty list) = restricted to nobody → deny.
/// </summary>
internal sealed class TenantGroupRoleRunbookAclFilter : IRunbookAclFilter
{
    public IReadOnlyList<RunbookSearchHit> Filter(
        IReadOnlyList<RunbookSearchHit> hits,
        RunbookCallerContext caller)
    {
        if (hits.Count == 0)
            return hits;

        List<RunbookSearchHit>? result = null;
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            if (IsAuthorized(hit, caller))
            {
                result ??= new List<RunbookSearchHit>(hits.Count);
                result.Add(hit);
            }
        }

        return result ?? [];
    }

    private static bool IsAuthorized(RunbookSearchHit hit, RunbookCallerContext caller)
    {
        // Open runbook: no restrictions → allow
        if (hit.AllowedGroups is null && hit.AllowedRoles is null)
            return true;

        // Check group membership (OR semantics across all allowed groups)
        if (hit.AllowedGroups is not null)
            foreach (var g in hit.AllowedGroups)
                if (caller.Groups.Contains(g, StringComparer.OrdinalIgnoreCase))
                    return true;

        // Check role membership (OR semantics across all allowed roles)
        if (hit.AllowedRoles is not null)
            foreach (var r in hit.AllowedRoles)
                if (caller.Roles.Contains(r, StringComparer.OrdinalIgnoreCase))
                    return true;

        return false;
    }
}
