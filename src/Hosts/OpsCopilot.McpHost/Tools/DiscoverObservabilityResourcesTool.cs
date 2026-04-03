using System.ComponentModel;
using System.Text.Json;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Exposes the "discover_observability_resources" MCP tool.
///
/// Discovers all Log Analytics workspaces across all accessible Azure subscriptions
/// via Azure Resource Graph, optionally enriched with their linked Application Insights
/// component when one exists.
///
/// Returns pairs of (subscriptionId, workspaceCustomerId, appInsightsName, appInsightsResourcePath)
/// suitable for multi-subscription workspace auto-discovery and for constructing
/// <c>| where _ResourceId has "..."</c> KQL filters in App Insights queries.
///
/// On success  → ok=true,  pairs populated, error=null.
/// On failure  → ok=false, pairs=[], error contains message.
/// </summary>
[McpServerToolType]
public sealed class DiscoverObservabilityResourcesTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    [McpServerTool(Name = "discover_observability_resources")]
    [Description(
        "Discovers all Azure Log Analytics workspaces across accessible Azure subscriptions via " +
        "Resource Graph, enriched with any linked Application Insights component when present. " +
        "Returns pairs of (subscriptionId, workspaceCustomerId, appInsightsName, appInsightsResourcePath). " +
        "Workspaces without a linked App Insights component are returned with empty appInsightsName/appInsightsResourcePath. " +
        "Pass subscriptionIds as a comma-separated list or leave empty to " +
        "query all accessible subscriptions. On success returns ok=true.")]
    public static async Task<string> ExecuteAsync(
        ArmClient      armClient,
        ILoggerFactory loggerFactory,

        [Description(
            "Optional comma-separated Azure subscription IDs to scope the discovery. " +
            "Leave empty or omit to discover across all accessible subscriptions.")]
        string? subscriptionIds,

        CancellationToken cancellationToken)
    {
        var logger        = loggerFactory.CreateLogger(nameof(DiscoverObservabilityResourcesTool));
        var executedAtUtc = DateTimeOffset.UtcNow;

        var subIds = (subscriptionIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // LAW-first join: all workspaces are returned regardless of whether an App Insights
        // component is linked. workspaceCustomerId is the GUID used as workspaceId in KQL.
        // appInsightsName/appInsightsResourcePath are populated only when a linked component exists.
        const string kql = """
            Resources
            | where type =~ 'microsoft.operationalinsights/workspaces'
            | extend customerId = tostring(properties.customerId), wsId = tolower(id)
            | join kind=leftouter (
                Resources
                | where type =~ 'microsoft.insights/components'
                | extend wsId = tolower(tostring(properties.WorkspaceResourceId))
                | project wsId,
                          aiName = tolower(name),
                          aiPath = strcat('/providers/microsoft.insights/components/', tolower(name))
              ) on wsId
            | project subscriptionId, resourceGroup,
                      workspaceCustomerId     = customerId,
                      appInsightsName         = coalesce(aiName, ''),
                      appInsightsResourcePath = coalesce(aiPath, '')
            | order by subscriptionId asc, workspaceCustomerId asc
            """;

        logger.LogInformation(
            "discover_observability_resources invoked | subscriptionScope={Scope}",
            subIds.Length == 0 ? "all" : $"{subIds.Length} subscription(s)");

        try
        {
            var queryContent = new ResourceQueryContent(kql);
            foreach (var id in subIds)
                queryContent.Subscriptions.Add(id);

            queryContent.Options = new ResourceQueryRequestOptions { ResultFormat = ResultFormat.Table };

            var tenantCollection = armClient.GetTenants();
            TenantResource? firstTenant = null;
            await foreach (var t in tenantCollection.GetAllAsync(cancellationToken: cancellationToken))
            {
                firstTenant = t;
                break;
            }

            if (firstTenant is null)
                throw new InvalidOperationException("No Azure tenant accessible via ArmClient.");

            var graphResponse = await firstTenant.GetResourcesAsync(queryContent, cancellationToken);
            using var doc     = JsonDocument.Parse(graphResponse.Value.Data.ToString());

            var pairs = ParseRows(doc.RootElement,
                row => (object)new
                {
                    subscriptionId          = row("subscriptionId"),
                    resourceGroup           = row("resourceGroup"),
                    workspaceCustomerId     = row("workspaceCustomerId"),
                    appInsightsName         = row("appInsightsName"),
                    appInsightsResourcePath = row("appInsightsResourcePath"),
                });

            logger.LogInformation(
                "discover_observability_resources completed | pairCount={Count}", pairs.Length);

            return JsonSerializer.Serialize(new
            {
                ok        = true,
                pairCount = pairs.Length,
                pairs,
                executedAtUtc,
                error     = (object?)null,
            }, JsonOpts);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("discover_observability_resources cancelled");
            return Fail(executedAtUtc, "Query was cancelled.", "OperationCancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "discover_observability_resources failed");
            return Fail(executedAtUtc, ex.Message, ex.GetType().Name);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T[] ParseRows<T>(
        JsonElement root,
        Func<Func<string, string>, T> selector)
    {
        var tableRoot = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rows", out _)
            ? root
            : root.TryGetProperty("data", out var d) ? d : root;

        if (!tableRoot.TryGetProperty("rows",    out var rowsEl) ||
            !tableRoot.TryGetProperty("columns", out var colsEl))
            return [];

        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var col in colsEl.EnumerateArray())
        {
            if (col.TryGetProperty("name", out var nameProp))
                cols[nameProp.GetString() ?? $"col{i}"] = i;
            i++;
        }

        var results = new List<T>();
        foreach (var row in rowsEl.EnumerateArray())
        {
            var cells = row.EnumerateArray().ToArray();
            string Get(string name) =>
                cols.TryGetValue(name, out var idx) && idx < cells.Length
                    ? cells[idx].ToString()
                    : string.Empty;

            results.Add(selector(Get));
        }
        return results.ToArray();
    }

    private static string Fail(DateTimeOffset executedAtUtc, string message, string errorType) =>
        JsonSerializer.Serialize(new
        {
            ok        = false,
            pairCount = 0,
            pairs     = Array.Empty<object>(),
            executedAtUtc,
            error     = $"[{errorType}] {message}",
        }, JsonOpts);
}
