namespace OpsCopilot.BuildingBlocks.Contracts.Tenancy;

/// <summary>
/// Cross-module contract that exposes tenant-specific governance configuration
/// resolved from the Tenancy module's SQL-backed store. Governance policies
/// consume this to apply per-tenant overrides without referencing Tenancy directly.
/// </summary>
public interface ITenantConfigProvider
{
    /// <summary>
    /// Returns governance-relevant configuration for the specified tenant,
    /// or <c>null</c> when the tenant ID is invalid or lookup fails.
    /// </summary>
    TenantGovernanceConfig? GetGovernanceConfig(string tenantId);
}
