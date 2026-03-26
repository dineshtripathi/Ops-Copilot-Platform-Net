namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Full resource inventory for a tenant: resource groups, App Insights components,
/// and Log Analytics workspaces enumerated via Azure Resource Graph.
/// </summary>
public sealed record TenantResourceInventory(
    string TenantId,
    IReadOnlyList<AzureResourceGroupSummary>    ResourceGroups,
    IReadOnlyList<AppInsightsComponentSummary>  AppInsightsComponents,
    IReadOnlyList<LogAnalyticsWorkspaceSummary> LogAnalyticsWorkspaces,
    string? Diagnostic = null);
