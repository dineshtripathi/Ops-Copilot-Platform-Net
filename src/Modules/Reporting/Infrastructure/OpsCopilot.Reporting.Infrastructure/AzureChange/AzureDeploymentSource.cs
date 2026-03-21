using System.Runtime.CompilerServices;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace OpsCopilot.Reporting.Infrastructure.AzureChange;

internal sealed class AzureDeploymentSource(ArmClient armClient) : IAzureDeploymentSource
{
    public async IAsyncEnumerable<DeploymentInfo> GetDeploymentsAsync(
        string subscriptionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sub = armClient.GetSubscriptionResource(
            SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        await foreach (var dep in sub.GetArmDeployments().GetAllAsync(cancellationToken: ct).WithCancellation(ct))
        {
            yield return new DeploymentInfo(
                dep.Data.Name,
                dep.Data.Properties?.Timestamp,
                dep.Data.Properties?.ProvisioningState?.ToString() ?? "Unknown",
                dep.Id.ResourceGroupName ?? "");
        }
    }
}
