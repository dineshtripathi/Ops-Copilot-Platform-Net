namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 93: Azure ARM deployment change signals — read-only, no subscription IDs logged.
/// </summary>
public sealed record AzureChangeSynthesis(
    int TotalDeployments,
    IReadOnlyList<AzureDeploymentSignal> Deployments);

/// <summary>Single ARM deployment snapshot — no subscription ID, no resource IDs.</summary>
public sealed record AzureDeploymentSignal(
    string DeploymentName,
    DateTimeOffset? Timestamp,
    string ProvisioningState,
    string ResourceGroup);
