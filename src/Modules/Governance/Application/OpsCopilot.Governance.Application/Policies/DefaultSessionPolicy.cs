using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Services;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Config-driven session policy. Resolves TTL using tenant-aware resolution.
/// Uses <see cref="ITenantAwareGovernanceOptionsResolver"/> for tenant-aware resolution.
/// </summary>
public sealed class DefaultSessionPolicy : ISessionPolicy
{
    private readonly ITenantAwareGovernanceOptionsResolver _resolver;

    public DefaultSessionPolicy(ITenantAwareGovernanceOptionsResolver resolver)
    {
        _resolver = resolver;
    }

    public TimeSpan GetSessionTtl(string tenantId)
    {
        var resolved = _resolver.Resolve(tenantId);
        return TimeSpan.FromMinutes(resolved.SessionTtlMinutes);
    }
}
