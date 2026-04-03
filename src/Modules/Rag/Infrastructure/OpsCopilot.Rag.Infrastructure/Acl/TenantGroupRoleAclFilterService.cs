using OpsCopilot.Rag.Application.Acl;
using OpsCopilot.Rag.Domain.Acl;

namespace OpsCopilot.Rag.Infrastructure.Acl;

/// <summary>
/// ACL filter that scopes vector search results to the requesting tenant.
/// Produces an OData-style filter expression that Azure AI Search can apply
/// as a server-side filter, preventing cross-tenant runbook exposure.
/// Slice 179 — §6.17 Tenant-Scoped RAG ACL.
/// </summary>
internal sealed class TenantGroupRoleAclFilterService : IAclFilterService
{
    /// <summary>
    /// Policy identifier recorded in every returned <see cref="AclFilter"/>.
    /// Allows audit tooling to trace which policy gate produced the filter.
    /// </summary>
    public const string PolicyId = "tenant-group-role";

    public Task<AclFilter> GetAclFilterAsync(
        string                  tenantId,
        IReadOnlyList<string>   userClaims,
        string                  env,
        CancellationToken       ct = default)
    {
        var filter = new AclFilter(
            FilterExpression: $"tenantId eq '{tenantId}'",
            AclPolicyId:      PolicyId);

        return Task.FromResult(filter);
    }
}
