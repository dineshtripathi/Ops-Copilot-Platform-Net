namespace OpsCopilot.Governance.Application.Configuration;

/// <summary>
/// Strongly-typed options bound to the "Governance" configuration section.
/// </summary>
public sealed class GovernanceOptions
{
    public const string SectionName = "Governance";

    /// <summary>Default governance settings applied to all tenants.</summary>
    public GovernanceDefaults Defaults { get; set; } = new();

    /// <summary>Per-tenant overrides keyed by tenant ID.</summary>
    public Dictionary<string, TenantOverride> TenantOverrides { get; set; } = new();
}

public sealed class GovernanceDefaults
{
    /// <summary>Tool names allowed for triage. Empty = allow all.</summary>
    public List<string> AllowedTools { get; set; } = ["kql_query"];

    /// <summary>Whether triage is enabled globally.</summary>
    public bool TriageEnabled { get; set; } = true;

    /// <summary>Maximum token budget per run (null = unlimited).</summary>
    public int? TokenBudget { get; set; }
}

public sealed class TenantOverride
{
    /// <summary>Tenant-specific allowed tools (overrides defaults when set).</summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>Tenant-specific triage toggle (overrides defaults when set).</summary>
    public bool? TriageEnabled { get; set; }

    /// <summary>Tenant-specific token budget (overrides defaults when set).</summary>
    public int? TokenBudget { get; set; }
}
