namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Lightweight projection of an Azure Log Analytics workspace discovered via Resource Graph.
/// </summary>
public sealed record LogAnalyticsWorkspaceSummary(
    string SubscriptionId,
    string ResourceGroup,
    string Name,
    string Location,
    string? CustomerId,
    int RetentionInDays,
    string? Sku);
