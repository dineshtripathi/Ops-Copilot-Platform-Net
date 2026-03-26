using System.ComponentModel;
using System.Text.Json;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Exposes the "list_subscriptions" MCP tool.
///
/// Lists all Azure subscriptions accessible to the managed identity,
/// optionally filtered by tenant ID.
///
/// Used by Reporting.Infrastructure to replace the direct Azure SDK
/// boundary violation in AzureTenantEstateProvider.
///
/// On success  → ok=true,  subscriptions populated, error=null.
/// On failure  → ok=false, subscriptions=[], error contains message.
/// </summary>
[McpServerToolType]
public sealed class ListSubscriptionsTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    [McpServerTool(Name = "list_subscriptions")]
    [Description(
        "Lists all Azure subscriptions accessible to the managed identity, filtered by tenant ID. " +
        "Returns subscriptionId, displayName, and state for each subscription. " +
        "Also returns accessible and active counts. " +
        "On success returns ok=true. On failure returns ok=false with an error field.")]
    public static async Task<string> ExecuteAsync(
        // Injected from DI — registered as singleton in Program.cs
        ArmClient        armClient,
        ILoggerFactory   loggerFactory,
        IHostEnvironment hostEnvironment,

        // MCP tool parameters
        [Description("Azure tenant ID (GUID). Only subscriptions belonging to this tenant are returned.")]
        string tenantId,

        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(ListSubscriptionsTool));

        // ── Input validation ─────────────────────────────────────────────────
        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Fail(tenantId,
                $"tenantId '{tenantId}' is not a valid GUID.", "ValidationError");
        }

        // ── List subscriptions ────────────────────────────────────────────────
        try
        {
            logger.LogDebug("list_subscriptions | tenantId={TenantId}", tenantId);

            var subscriptions = new List<object>();
            var activeCount   = 0;

            await foreach (var sub in armClient.GetSubscriptions().GetAllAsync(cancellationToken))
            {
                // Filter by tenant — TenantId is Guid? and may be null in ARM API responses.
                // The ARM REST GET /subscriptions endpoint does not always populate tenantId
                // in the response body, so treat null as "belongs to the authenticated tenant"
                // rather than silently discarding every subscription.
                if (sub.Data.TenantId.HasValue && sub.Data.TenantId.Value != tenantGuid) continue;

                var state = sub.Data.State?.ToString() ?? "Unknown";
                if (string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase))
                    activeCount++;

                subscriptions.Add(new
                {
                    subscriptionId = sub.Data.SubscriptionId,
                    displayName    = sub.Data.DisplayName,
                    state,
                });
            }

            // Order by display name for deterministic output
            subscriptions = subscriptions
                .OrderBy(s => ((dynamic)s).displayName as string)
                .ToList<object>();

            var result = new
            {
                ok                      = true,
                tenantId,
                subscriptionCount       = subscriptions.Count,
                accessible              = subscriptions.Count,
                active                  = activeCount,
                subscriptions,
                diagnostic              = (string?)null,
                error                   = (string?)null,
            };

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "list_subscriptions failed | tenantId={TenantId}", tenantId);
            return Fail(tenantId, ex.Message, ex.GetType().Name);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Fail(string tenantId, string message, string errorType)
    {
        var payload = new
        {
            ok                = false,
            tenantId,
            subscriptionCount = 0,
            accessible        = 0,
            active            = 0,
            subscriptions     = Array.Empty<object>(),
            diagnostic        = (string?)null,
            error             = $"[{errorType}] {message}",
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }
}
