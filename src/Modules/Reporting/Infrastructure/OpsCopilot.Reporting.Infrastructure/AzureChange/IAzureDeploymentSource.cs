namespace OpsCopilot.Reporting.Infrastructure.AzureChange;

internal interface IAzureDeploymentSource
{
    IAsyncEnumerable<DeploymentInfo> GetDeploymentsAsync(string subscriptionId, CancellationToken ct);
}

internal sealed record DeploymentInfo(
    string Name,
    DateTimeOffset? Timestamp,
    string ProvisioningState,
    string ResourceGroup);
