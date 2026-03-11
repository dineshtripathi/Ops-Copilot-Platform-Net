namespace OpsCopilot.BuildingBlocks.Contracts.Rag;

/// <summary>
/// Identifies the caller for runbook ACL enforcement.
/// Populated from the tenant identity claims present at run time.
/// Groups and Roles are empty in deployments where claim propagation
/// has not yet been wired; the ACL filter must tolerate empty collections.
/// </summary>
public sealed record RunbookCallerContext(
    string TenantId,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Roles)
{
    /// <summary>
    /// Convenience factory for a tenant-only context (no group/role claims).
    /// Used during the transition period before caller claims are propagated.
    /// </summary>
    public static RunbookCallerContext TenantOnly(string tenantId) =>
        new(tenantId, [], []);
}
