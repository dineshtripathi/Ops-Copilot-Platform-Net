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
/// Exposes the "list_log_analytics_workspaces" MCP tool.
///
/// Enumerates all Log Analytics workspaces (microsoft.operationalinsights/workspaces)
/// across the given subscriptions via Azure Resource Graph.
///
/// On success  → ok=true,  workspaces populated, error=null.
/// On failure  → ok=false, workspaces=[], error contains message.
/// </summary>
[McpServerToolType]
public sealed class ListLogAnalyticsWorkspacesTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    [McpServerTool(Name = "list_log_analytics_workspaces")]
    [Description(
        "Lists all Azure Log Analytics workspaces across the given subscriptions " +
        "via Resource Graph. Returns subscriptionId, resourceGroup, name, location, " +
        "customerId, retentionInDays, and sku for each workspace. " +
        "On success returns ok=true. On failure returns ok=false with an error field.")]
    public static async Task<string> ExecuteAsync(
        ArmClient       armClient,
        ILoggerFactory  loggerFactory,

        [Description("Comma-separated list of Azure subscription IDs to scope the query.")]
        string subscriptionIds,

        CancellationToken cancellationToken)
    {
        var logger        = loggerFactory.CreateLogger(nameof(ListLogAnalyticsWorkspacesTool));
        var executedAtUtc = DateTimeOffset.UtcNow;

        var subIds = subscriptionIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (subIds.Length == 0)
            return Fail(executedAtUtc, "subscriptionIds must not be empty.", "ValidationError");

        const string kql = """
            Resources
            | where type =~ 'microsoft.operationalinsights/workspaces'
            | project subscriptionId, resourceGroup, name, location,
                      customerId      = tostring(properties.customerId),
                      retentionInDays = toint(properties.retentionInDays),
                      sku             = tostring(properties.sku.name)
            | order by subscriptionId asc, resourceGroup asc, name asc
            """;

        logger.LogInformation(
            "list_log_analytics_workspaces invoked | subscriptionCount={Count}", subIds.Length);

        try
        {
            var queryContent = new ResourceQueryContent(kql);
            foreach (var id in subIds)
                queryContent.Subscriptions.Add(id);

            // Force tabular result format so ParseRows can read columns/rows correctly.
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

            var workspaces = ParseRows(doc.RootElement,
                row => (object)new
                {
                    subscriptionId  = row("subscriptionId"),
                    resourceGroup   = row("resourceGroup"),
                    name            = row("name"),
                    location        = row("location"),
                    customerId      = row("customerId"),
                    retentionInDays = TryParseInt(row("retentionInDays")),
                    sku             = row("sku"),
                });

            logger.LogInformation(
                "list_log_analytics_workspaces completed | count={Count}", workspaces.Length);

            return JsonSerializer.Serialize(new
            {
                ok             = true,
                workspaceCount = workspaces.Length,
                workspaces,
                executedAtUtc,
                error          = (object?)null,
            }, JsonOpts);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("list_log_analytics_workspaces cancelled");
            return Fail(executedAtUtc, "Query was cancelled.", "OperationCancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "list_log_analytics_workspaces failed");
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

        if (!tableRoot.TryGetProperty("rows", out var rowsEl) ||
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

    private static int TryParseInt(string value) =>
        int.TryParse(value, out var n) ? n : 0;

    private static string Fail(DateTimeOffset executedAtUtc, string message, string errorType) =>
        JsonSerializer.Serialize(new
        {
            ok             = false,
            workspaceCount = 0,
            workspaces     = Array.Empty<object>(),
            executedAtUtc,
            error          = $"[{errorType}] {message}",
        }, JsonOpts);
}
