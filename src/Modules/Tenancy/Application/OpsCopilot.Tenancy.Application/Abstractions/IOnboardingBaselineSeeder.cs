namespace OpsCopilot.Tenancy.Application.Abstractions;

/// <summary>
/// Seeds default governance configuration entries for a newly onboarded tenant.
/// §6.19 — Onboarding Orchestration (baseline generation + tenant config population).
/// </summary>
public interface IOnboardingBaselineSeeder
{
    /// <summary>
    /// Writes governance defaults into the tenant config store for <paramref name="tenantId"/>.
    /// Returns the list of config keys that were seeded.
    /// </summary>
    Task<IReadOnlyList<string>> SeedAsync(
        Guid tenantId,
        string? seededBy,
        CancellationToken ct = default);
}
