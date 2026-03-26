using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Infrastructure.McpClient;

namespace OpsCopilot.Reporting.Infrastructure.AzureChange;

/// <summary>
/// Retrieves ARM deployment history via the McpHost <c>list_arm_deployments</c> tool.
///
/// Replaces <see cref="AzureDeploymentSource"/> to remove the direct Azure.ResourceManager
/// dependency from the Reporting.Infrastructure module boundary.
/// </summary>
internal sealed class McpDeploymentSource(
    ReportingMcpHostClient          mcp,
    ILogger<McpDeploymentSource>    logger) : IAzureDeploymentSource
{
    public async IAsyncEnumerable<DeploymentInfo> GetDeploymentsAsync(
        string subscriptionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var json = await mcp.CallToolAsync(
            "list_arm_deployments",
            new Dictionary<string, object?> { ["subscriptionId"] = subscriptionId, ["tenantId"] = "" },
            ct);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "list_arm_deployments returned non-JSON for subscription {Sub}.", subscriptionId);
            yield break;
        }

        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var err = root.TryGetProperty("error", out var ep)
                ? (ep.ValueKind == JsonValueKind.String ? ep.GetString() ?? "(no error message)" : ep.GetRawText())
                : "(no error message)";
            logger.LogWarning("list_arm_deployments failed for subscription {Sub}: {Error}", subscriptionId, err);
            yield break;
        }

        if (!root.TryGetProperty("deployments", out var deployments)
            || deployments.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var dep in deployments.EnumerateArray())
        {
            var name  = dep.TryGetProperty("name",               out var n) ? n.GetString() ?? "" : "";
            var state = dep.TryGetProperty("provisioningState",   out var s) ? s.GetString() ?? "Unknown" : "Unknown";
            var rg    = dep.TryGetProperty("resourceGroup",       out var r) ? r.GetString() ?? "" : "";

            DateTimeOffset? timestamp = null;
            if (dep.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(ts.GetString(), out var parsed))
                    timestamp = parsed;
            }

            yield return new DeploymentInfo(name, timestamp, state, rg);
        }
    }
}
