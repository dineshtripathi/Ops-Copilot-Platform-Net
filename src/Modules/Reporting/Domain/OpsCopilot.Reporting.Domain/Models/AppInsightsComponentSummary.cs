namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Lightweight projection of an Azure Application Insights component discovered via Resource Graph.
/// </summary>
public sealed record AppInsightsComponentSummary(
    string SubscriptionId,
    string ResourceGroup,
    string Name,
    string Location,
    string Kind,
    string? WorkspaceResourceId);
