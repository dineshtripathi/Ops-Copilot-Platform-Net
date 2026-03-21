using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.AzureChange;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 93 — reads Azure ARM deployment properties and maps them to
/// AzureChangeSynthesis using deterministic state inspection (no LLM, no mutations,
/// no subscription IDs or resource IDs ever logged).
///
/// Deployments are capped at <see cref="MaxDeployments"/> to bound the per-request latency.
/// Exceptions are caught and logged as warnings so callers receive null (graceful degradation).
/// </summary>
internal sealed class AzureChangeEvidenceProvider(
    IAzureDeploymentSource source,
    ILogger<AzureChangeEvidenceProvider> logger) : IAzureChangeEvidenceProvider
{
    private const int MaxDeployments = 20;

    public async Task<AzureChangeSynthesis?> GetSynthesisAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        try
        {
            var deployments = new List<AzureDeploymentSignal>();

            await foreach (var d in source.GetDeploymentsAsync(tenantId, ct))
            {
                if (deployments.Count >= MaxDeployments)
                    break;

                deployments.Add(new AzureDeploymentSignal(
                    d.Name,
                    d.Timestamp,
                    d.ProvisioningState,
                    d.ResourceGroup));
            }

            return new AzureChangeSynthesis(deployments.Count, deployments);
        }
        catch (Exception ex)
        {
            // Log identifier only — no subscription ID, no payload, no token.
            logger.LogWarning(ex,
                "Azure deployment evidence collection failed for run {RunId}", runId);
            return null;
        }
    }
}
