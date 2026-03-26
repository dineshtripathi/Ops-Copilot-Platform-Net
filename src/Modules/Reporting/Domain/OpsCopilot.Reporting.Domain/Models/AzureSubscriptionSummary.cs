namespace OpsCopilot.Reporting.Domain.Models;

public sealed record AzureSubscriptionSummary(
    string SubscriptionId,
    string DisplayName,
    string State);