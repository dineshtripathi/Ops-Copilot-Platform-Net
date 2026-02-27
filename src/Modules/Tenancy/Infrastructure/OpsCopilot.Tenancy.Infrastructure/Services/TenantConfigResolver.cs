using System.Text.Json;
using Microsoft.Extensions.Options;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.Configuration;
using OpsCopilot.Tenancy.Application.DTOs;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

public sealed class TenantConfigResolver : ITenantConfigResolver
{
    private readonly ITenantConfigStore _store;
    private readonly GovernanceDefaultsConfig _defaults;

    public TenantConfigResolver(ITenantConfigStore store, IOptions<GovernanceDefaultsConfig> defaults)
    {
        _store = store;
        _defaults = defaults.Value;
    }

    public async Task<EffectiveTenantConfig> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var entries = await _store.GetAsync(tenantId, ct);
        var dict = entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);

        return new EffectiveTenantConfig(
            AllowedTools: dict.TryGetValue("AllowedTools", out var at)
                ? JsonSerializer.Deserialize<List<string>>(at) ?? _defaults.AllowedTools
                : _defaults.AllowedTools,
            TriageEnabled: dict.TryGetValue("TriageEnabled", out var te)
                ? bool.TryParse(te, out var teVal) && teVal
                : _defaults.TriageEnabled,
            TokenBudget: dict.TryGetValue("TokenBudget", out var tb)
                ? int.TryParse(tb, out var tbVal) ? tbVal : _defaults.TokenBudget
                : _defaults.TokenBudget,
            SessionTtlMinutes: dict.TryGetValue("SessionTtlMinutes", out var st)
                ? int.TryParse(st, out var stVal) ? stVal : _defaults.SessionTtlMinutes
                : _defaults.SessionTtlMinutes
        );
    }
}
