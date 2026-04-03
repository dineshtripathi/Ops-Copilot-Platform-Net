using OpsCopilot.Rag.Application.Acl;
using OpsCopilot.Rag.Domain.Acl;

namespace OpsCopilot.Rag.Infrastructure.Acl;

/// <summary>
/// ACL filter that scopes vector search results to the requesting tenant
/// and further restricts to runbooks tagged for the caller's Entra group memberships.
/// Group IDs are sourced directly from the caller's JWT claims (no Microsoft Graph call required).
///
/// Filter encoding convention:
///   – Runbooks accessible to all tenant members carry <c>alltenants</c> in their Tags field.
///   – Group-restricted runbooks carry <c>group:{groupId}</c> entries in their Tags field.
///
/// When <paramref name="userClaims"/> is empty the filter degrades gracefully to a
/// tenant-only scope, identical in security level to <see cref="TenantGroupRoleAclFilterService"/>.
///
/// Slice 191 — §6.18 Entra-Group-Aware RAG ACL.
/// </summary>
internal sealed class EntraGroupAclFilterService : IAclFilterService
{
    /// <summary>
    /// Tag value that marks a runbook as accessible to every authenticated member of the tenant.
    /// Must be present in <see cref="VectorRunbookDocument.Tags"/> at index time.
    /// </summary>
    internal const string AllTenantsTag = "alltenants";

    /// <summary>
    /// Policy identifier recorded in every returned <see cref="AclFilter"/>.
    /// Allows audit tooling to trace which policy gate produced the filter.
    /// </summary>
    public const string PolicyId = "entra-group-role";

    public Task<AclFilter> GetAclFilterAsync(
        string                  tenantId,
        IReadOnlyList<string>   userClaims,
        string                  env,
        CancellationToken       ct = default)
    {
        var expression = BuildFilter(tenantId, userClaims);
        return Task.FromResult(new AclFilter(expression, PolicyId));
    }

    private static string BuildFilter(string tenantId, IReadOnlyList<string> userClaims)
    {
        var tenantClause = $"tenantId eq '{tenantId}'";

        if (userClaims.Count == 0)
            return tenantClause;

        // Each Entra group ID is encoded as "group:{id}" in the Tags string field.
        // The alltenants sentinel allows runbooks that are not group-restricted.
        var groupClauses = userClaims
            .Select(gid => $"contains(tags, 'group:{gid}')")
            .Append($"contains(tags, '{AllTenantsTag}')");

        var groupFilter = string.Join(" or ", groupClauses);
        return $"{tenantClause} and ({groupFilter})";
    }
}
