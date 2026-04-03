using System.ComponentModel;
using System.Text.Json;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Exposes the "list_arm_deployments" MCP tool.
///
/// Lists ARM deployments for an Azure subscription using the shared
/// <see cref="ArmClient"/> singleton.
///
/// Used by Reporting.Infrastructure to replace the direct Azure SDK
/// boundary violation in AzureDeploymentSource.
///
/// On success  → ok=true,  deployments populated, error=null.
/// On failure  → ok=false, deployments=[], error contains message.
/// </summary>
[McpServerToolType]
public sealed class ListArmDeploymentsTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Maximum deployments to return per call (keeps payload bounded).</summary>
    private const int MaxRows = 500;

    [McpServerTool(Name = "list_arm_deployments")]
    [Description(
        "Lists ARM deployments for an Azure subscription. " +
        "Returns name, timestamp, provisioningState, and resourceGroup for each deployment. " +
        "Capped at 500 results. " +
        "On success returns ok=true. On failure returns ok=false with an error field.")]
    public static async Task<string> ExecuteAsync(
        // Injected from DI — registered as singleton in Program.cs
        ArmClient        armClient,
        ILoggerFactory   loggerFactory,
        IHostEnvironment hostEnvironment,

        // MCP tool parameters
        [Description("Azure subscription ID (GUID).")]
        string subscriptionId,

        [Description("Azure tenant ID (GUID). Used for audit context only; not used for filtering.")]
        string tenantId,

        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(ListArmDeploymentsTool));

        // ── Input validation ─────────────────────────────────────────────────
        if (!Guid.TryParse(subscriptionId, out _))
        {
            return Fail(subscriptionId, tenantId,
                $"subscriptionId '{subscriptionId}' is not a valid GUID.", "ValidationError");
        }

        // ── List deployments ──────────────────────────────────────────────────
        try
        {
            logger.LogDebug(
                "list_arm_deployments | subscriptionId={SubscriptionId}",
                subscriptionId);

            var sub = armClient.GetSubscriptionResource(
                SubscriptionResource.CreateResourceIdentifier(subscriptionId));

            var deployments = new List<object>();
            await foreach (var dep in sub.GetArmDeployments().GetAllAsync(cancellationToken: cancellationToken))
            {
                if (deployments.Count >= MaxRows) break;

                deployments.Add(new
                {
                    name              = dep.Data.Name,
                    timestamp         = dep.Data.Properties?.Timestamp,
                    provisioningState = dep.Data.Properties?.ProvisioningState?.ToString() ?? "Unknown",
                    resourceGroup     = dep.Id.ResourceGroupName ?? string.Empty,
                });
            }

            var result = new
            {
                ok = true,
                subscriptionId,
                tenantId,
                deployments,
                error = (string?)null,
            };

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "list_arm_deployments failed | subscriptionId={SubscriptionId}", subscriptionId);
            return Fail(subscriptionId, tenantId, ex.Message, ex.GetType().Name);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Fail(string subscriptionId, string tenantId, string message, string errorType)
    {
        var payload = new
        {
            ok             = false,
            subscriptionId,
            tenantId,
            deployments    = Array.Empty<object>(),
            error          = $"[{errorType}] {message}",
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }
}
