using OpsCopilot.Rag.Domain.Acl;

namespace OpsCopilot.Rag.Application.Acl;

/// <summary>
/// Single source-of-truth for ACL metadata and rules (§6.17).
/// Resolves a filter expression for the given tenant and user context.
/// The filter is applied by the Vector Store Access Layer (§6.8) — not here.
/// </summary>
public interface IAclFilterService
{
    /// <summary>
    /// Returns the <see cref="AclFilter"/> that should be applied to vector store
    /// queries for the specified caller context.
    /// </summary>
    /// <param name="tenantId">Tenant making the request.</param>
    /// <param name="userClaims">Claims from the caller's identity token.</param>
    /// <param name="env">Deployment environment (e.g. "production", "staging").</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AclFilter> GetAclFilterAsync(
        string                  tenantId,
        IReadOnlyList<string>   userClaims,
        string                  env,
        CancellationToken       ct = default);
}
