using OpsCopilot.BuildingBlocks.Contracts.Tenancy;
using OpsCopilot.Tenancy.Application.Abstractions;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

/// <summary>
/// Adapts the Tenancy module's <see cref="ITenantConfigResolver"/> to the
/// cross-module <see cref="ITenantConfigProvider"/> contract so Governance
/// policies can consume SQL-backed tenant config without referencing Tenancy directly.
/// </summary>
public sealed class TenantConfigProviderAdapter : ITenantConfigProvider
{
    private readonly ITenantConfigResolver _resolver;

    public TenantConfigProviderAdapter(ITenantConfigResolver resolver)
    {
        _resolver = resolver;
    }

    public TenantGovernanceConfig? GetGovernanceConfig(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var id))
            return null;

        try
        {
            var config = _resolver.ResolveAsync(id).GetAwaiter().GetResult();

            return new TenantGovernanceConfig(
                config.AllowedTools,
                config.TokenBudget,
                config.SessionTtlMinutes);
        }
        catch
        {
            // Graceful degradation — if SQL is unreachable the caller
            // falls back to config-file settings.
            return null;
        }
    }
}
