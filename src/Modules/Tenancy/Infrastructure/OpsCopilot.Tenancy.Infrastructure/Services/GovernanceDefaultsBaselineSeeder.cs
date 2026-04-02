using System.Text.Json;
using Microsoft.Extensions.Options;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.Configuration;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

/// <summary>
/// Seeds the governance defaults from <see cref="GovernanceDefaultsConfig"/> into
/// the tenant's config store during onboarding.
/// §6.19 — Onboarding Orchestration (baseline generation + tenant config population).
/// </summary>
public sealed class GovernanceDefaultsBaselineSeeder : IOnboardingBaselineSeeder
{
    private readonly ITenantConfigStore _configStore;
    private readonly GovernanceDefaultsConfig _defaults;

    public GovernanceDefaultsBaselineSeeder(
        ITenantConfigStore configStore,
        IOptions<GovernanceDefaultsConfig> options)
    {
        _configStore = configStore;
        _defaults    = options.Value;
    }

    public async Task<IReadOnlyList<string>> SeedAsync(
        Guid tenantId,
        string? seededBy,
        CancellationToken ct = default)
    {
        var seeded = new List<string>();

        await _configStore.UpsertAsync(
            tenantId,
            "AllowedTools",
            JsonSerializer.Serialize(_defaults.AllowedTools),
            seededBy,
            ct);
        seeded.Add("AllowedTools");

        await _configStore.UpsertAsync(
            tenantId,
            "TriageEnabled",
            _defaults.TriageEnabled.ToString(),
            seededBy,
            ct);
        seeded.Add("TriageEnabled");

        if (_defaults.TokenBudget.HasValue)
        {
            await _configStore.UpsertAsync(
                tenantId,
                "TokenBudget",
                _defaults.TokenBudget.Value.ToString(),
                seededBy,
                ct);
            seeded.Add("TokenBudget");
        }

        await _configStore.UpsertAsync(
            tenantId,
            "SessionTtlMinutes",
            _defaults.SessionTtlMinutes.ToString(),
            seededBy,
            ct);
        seeded.Add("SessionTtlMinutes");

        return seeded;
    }
}
