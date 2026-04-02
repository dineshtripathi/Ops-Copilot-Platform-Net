using OpsCopilot.Rag.Application.Acl;
using OpsCopilot.Rag.Domain.Acl;

namespace OpsCopilot.Rag.Infrastructure.Acl;

/// <summary>
/// Passthrough ACL filter — allows all requests without restriction.
/// Registered when no policy store is configured (development / bootstrap mode).
/// Replace with a policy-backed implementation to enable real ACL enforcement.
/// </summary>
internal sealed class NullAclFilterService : IAclFilterService
{
    public Task<AclFilter> GetAclFilterAsync(
        string                tenantId,
        IReadOnlyList<string> userClaims,
        string                env,
        CancellationToken     ct = default)
        => Task.FromResult(new AclFilter(FilterExpression: string.Empty, AclPolicyId: string.Empty));
}
