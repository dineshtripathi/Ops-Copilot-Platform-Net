namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Lightweight projection of an Azure resource group discovered via Resource Graph.
/// </summary>
public sealed record AzureResourceGroupSummary(
    string SubscriptionId,
    string Name,
    string Location,
    string ProvisioningState);
