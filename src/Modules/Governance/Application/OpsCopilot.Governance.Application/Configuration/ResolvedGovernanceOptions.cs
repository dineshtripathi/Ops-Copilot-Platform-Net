namespace OpsCopilot.Governance.Application.Configuration;

/// <summary>
/// Immutable snapshot of governance settings resolved for a specific tenant.
/// Priority: SQL-backed tenant config → config-file TenantOverrides → Defaults.
/// </summary>
public sealed record ResolvedGovernanceOptions(
    List<string> AllowedTools,
    int? TokenBudget,
    int SessionTtlMinutes);
