using Microsoft.Extensions.Options;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Configuration;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Config-driven session policy. Resolves TTL from GovernanceOptions
/// with per-tenant override support.
/// </summary>
public sealed class DefaultSessionPolicy : ISessionPolicy
{
    private readonly GovernanceOptions _options;

    public DefaultSessionPolicy(IOptions<GovernanceOptions> options)
    {
        _options = options.Value;
    }

    public TimeSpan GetSessionTtl(string tenantId)
    {
        var minutes = ResolveSessionTtlMinutes(tenantId);
        return TimeSpan.FromMinutes(minutes);
    }

    private int ResolveSessionTtlMinutes(string tenantId)
    {
        if (_options.TenantOverrides.TryGetValue(tenantId, out var tenantOverride)
            && tenantOverride.SessionTtlMinutes is not null)
        {
            return tenantOverride.SessionTtlMinutes.Value;
        }

        return _options.Defaults.SessionTtlMinutes;
    }
}
