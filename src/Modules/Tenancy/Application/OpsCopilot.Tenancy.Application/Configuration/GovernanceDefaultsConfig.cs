namespace OpsCopilot.Tenancy.Application.Configuration;

/// <summary>
/// Mirrors the Governance:Defaults section from appsettings.json.
/// Used by the tenant config resolver as fallback defaults.
/// Avoids a cross-module dependency on Governance.Application.
/// </summary>
public sealed class GovernanceDefaultsConfig
{
    public const string SectionName = "Governance:Defaults";

    public List<string> AllowedTools { get; set; } = new();
    public bool TriageEnabled { get; set; }
    public int? TokenBudget { get; set; }
    public int SessionTtlMinutes { get; set; } = 30;
}
