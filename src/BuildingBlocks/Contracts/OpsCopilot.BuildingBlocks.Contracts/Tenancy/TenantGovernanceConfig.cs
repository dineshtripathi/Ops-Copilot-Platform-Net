namespace OpsCopilot.BuildingBlocks.Contracts.Tenancy;

/// <summary>
/// Immutable snapshot of governance-relevant tenant configuration
/// resolved from the SQL-backed tenant config store.
/// </summary>
public sealed record TenantGovernanceConfig(
    List<string> AllowedTools,
    int? TokenBudget,
    int SessionTtlMinutes);
