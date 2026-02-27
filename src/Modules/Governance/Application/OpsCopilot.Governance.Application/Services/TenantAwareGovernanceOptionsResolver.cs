using Microsoft.Extensions.Options;
using OpsCopilot.BuildingBlocks.Contracts.Tenancy;
using OpsCopilot.Governance.Application.Configuration;

namespace OpsCopilot.Governance.Application.Services;

/// <summary>
/// Resolves governance options for a specific tenant using a three-tier priority:
/// 1. SQL-backed tenant config (via <see cref="ITenantConfigProvider"/>)
/// 2. Config-file TenantOverrides in <see cref="GovernanceOptions"/>
/// 3. GovernanceOptions.Defaults
/// </summary>
public interface ITenantAwareGovernanceOptionsResolver
{
    ResolvedGovernanceOptions Resolve(string tenantId);
}

public sealed class TenantAwareGovernanceOptionsResolver : ITenantAwareGovernanceOptionsResolver
{
    private readonly GovernanceOptions _options;
    private readonly ITenantConfigProvider? _provider;

    public TenantAwareGovernanceOptionsResolver(
        IOptions<GovernanceOptions> options,
        ITenantConfigProvider? provider = null)
    {
        _options = options.Value;
        _provider = provider;
    }

    public ResolvedGovernanceOptions Resolve(string tenantId)
    {
        // Priority 1: SQL-backed tenant config via ITenantConfigProvider
        if (_provider is not null)
        {
            try
            {
                var tenantConfig = _provider.GetGovernanceConfig(tenantId);
                if (tenantConfig is not null)
                {
                    return new ResolvedGovernanceOptions(
                        tenantConfig.AllowedTools,
                        tenantConfig.TokenBudget,
                        tenantConfig.SessionTtlMinutes);
                }
            }
            catch
            {
                // Graceful degradation: if SQL lookup fails, fall through to config-file
            }
        }

        // Priority 2: Config-file TenantOverrides
        if (_options.TenantOverrides.TryGetValue(tenantId, out var ov))
        {
            return new ResolvedGovernanceOptions(
                ov.AllowedTools ?? _options.Defaults.AllowedTools,
                ov.TokenBudget ?? _options.Defaults.TokenBudget,
                ov.SessionTtlMinutes ?? _options.Defaults.SessionTtlMinutes);
        }

        // Priority 3: Defaults
        return new ResolvedGovernanceOptions(
            _options.Defaults.AllowedTools,
            _options.Defaults.TokenBudget,
            _options.Defaults.SessionTtlMinutes);
    }
}
