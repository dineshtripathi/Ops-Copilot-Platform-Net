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
/// Exposes the "list_app_insights" MCP tool.
///
/// Enumerates all Application Insights components (microsoft.insights/components)
/// across the given subscriptions via Azure Resource Graph.
///
/// On success  → ok=true,  components populated, error=null.
/// On failure  → ok=false, components=[], error contains message.
/// </summary>
[McpServerToolType]
public sealed class ListAppInsightsTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    [McpServerTool(Name = "list_app_insights")]
    [Description(
        "Lists all Azure Application Insights components across the given subscriptions " +
        "via Resource Graph. Returns subscriptionId, resourceGroup, name, location, kind, " +
        "and workspaceResourceId for each component. " +
        "On success returns ok=true. On failure returns ok=false with an error field.")]
    public static async Task<string> ExecuteAsync(
        ArmClient       armClient,
        ILoggerFactory  loggerFactory,

        [Description("Comma-separated list of Azure subscription IDs to scope the query.")]
        string subscriptionIds,

        CancellationToken cancellationToken)
    {
        var logger        = loggerFactory.CreateLogger(nameof(ListAppInsightsTool));
        var executedAtUtc = DateTimeOffset.UtcNow;

        var subIds = subscriptionIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (subIds.Length == 0)
            return Fail(executedAtUtc, "subscriptionIds must not be empty.", "ValidationError");

        const string kql = """
            Resources
            | where type =~ 'microsoft.insights/components'
            | project subscriptionId, resourceGroup, name, location, kind,
                      workspaceResourceId = tostring(properties.WorkspaceResourceId)
            | order by subscriptionId asc, resourceGroup asc, name asc
            """;

        logger.LogInformation("list_app_insights invoked | subscriptionCount={Count}", subIds.Length);

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

            var components = ParseRows(doc.RootElement,
                row => (object)new
                {
                    subscriptionId      = row("subscriptionId"),
                    resourceGroup       = row("resourceGroup"),
                    name                = row("name"),
                    location            = row("location"),
                    kind                = row("kind"),
                    workspaceResourceId = row("workspaceResourceId"),
                });

            logger.LogInformation("list_app_insights completed | count={Count}", components.Length);

            return JsonSerializer.Serialize(new
            {
                ok             = true,
                componentCount = components.Length,
                components,
                executedAtUtc,
                error          = (object?)null,
            }, JsonOpts);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("list_app_insights cancelled");
            return Fail(executedAtUtc, "Query was cancelled.", "OperationCancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "list_app_insights failed");
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

    private static string Fail(DateTimeOffset executedAtUtc, string message, string errorType) =>
        JsonSerializer.Serialize(new
        {
            ok             = false,
            componentCount = 0,
            components     = Array.Empty<object>(),
            executedAtUtc,
            error          = $"[{errorType}] {message}",
        }, JsonOpts);
}
