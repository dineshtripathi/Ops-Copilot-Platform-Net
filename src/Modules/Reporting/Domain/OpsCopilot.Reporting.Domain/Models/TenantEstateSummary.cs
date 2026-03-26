namespace OpsCopilot.Reporting.Domain.Models;

public sealed record TenantEstateSummary(
    string TenantId,
    int AccessibleSubscriptionCount,
    int ActiveSubscriptionCount,
    IReadOnlyList<AzureSubscriptionSummary> Subscriptions,
    string? Diagnostic = null);