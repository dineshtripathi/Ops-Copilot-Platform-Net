using System.ComponentModel;
using System.Text.Json;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Exposes the "deployment_diff" MCP tool.
///
/// Discovered automatically by <c>WithToolsFromAssembly()</c>
/// via the <see cref="McpServerToolTypeAttribute"/> marker.
///
/// Queries the Azure Resource Graph <c>resourcechanges</c> table to surface
/// resource creations, updates, and deletions within a lookback window.
/// Results are used as governed change-evidence citations in triage runs.
///
/// On success  → ok=true,  changes populated, error=null.
/// On failure  → ok=false, changes=[], error contains message.
/// </summary>
[McpServerToolType]
public sealed class DeploymentDiffTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Maximum resource-change rows to return per call (keeps payload bounded).</summary>
    private const int MaxRows = 200;

    [McpServerTool(Name = "deployment_diff")]
    [Description(
        "Returns Azure Resource Graph resource changes for a subscription within a lookback window. " +
        "Lists resource modifications, creations, and deletions to provide governed change evidence " +
        "for incident triage. Optionally filtered to a single resource group. " +
        "On success returns ok=true with a changes array. On failure returns ok=false with error.")]
    public static async Task<string> ExecuteAsync(
        // Injected from DI — registered as singleton in Program.cs
        ArmClient        armClient,
        ILoggerFactory   loggerFactory,
        IHostEnvironment hostEnvironment,

        // MCP tool parameters — appear in the JSON input schema
        [Description("Azure tenant ID (GUID).")]
        string tenantId,

        [Description(
            "Azure subscription ID (GUID) to query, e.g. " +
            "'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx'.")]
        string subscriptionId,

        [Description(
            "Optional resource group name to narrow the change scope. " +
            "Pass null or omit to query the whole subscription.")]
        string? resourceGroup,

        [Description(
            "How far back to look for changes, in minutes. " +
            "For example 120 returns changes in the last 2 hours. " +
            "Minimum 1; maximum 1440 (24 h).")]
        int lookbackMinutes,

        CancellationToken cancellationToken)
    {
        var executedAtUtc = DateTimeOffset.UtcNow;
        var logger        = loggerFactory.CreateLogger(nameof(DeploymentDiffTool));

        // ── Input validation ─────────────────────────────────────────────────
        if (!Guid.TryParse(tenantId, out _))
            return Fail(subscriptionId, executedAtUtc,
                $"tenantId '{tenantId}' is not a valid GUID.", "ValidationError");

        if (!Guid.TryParse(subscriptionId, out _))
            return Fail(subscriptionId, executedAtUtc,
                $"subscriptionId '{subscriptionId}' is not a valid GUID.", "ValidationError");

        lookbackMinutes = Math.Clamp(lookbackMinutes, 1, 1440);

        // ── Build KQL query ──────────────────────────────────────────────────
        var startTime = executedAtUtc.AddMinutes(-lookbackMinutes);
        var startIso  = startTime.ToString("o");  // ISO 8601 UTC

        var rgFilter = string.IsNullOrWhiteSpace(resourceGroup)
            ? string.Empty
            : $"| where resourceGroup =~ '{EscapeKqlString(resourceGroup)}'";

        var query = $"""
            resourcechanges
            | where subscriptionId =~ '{EscapeKqlString(subscriptionId)}'
            | where changeTime >= todatetime('{startIso}')
            {rgFilter}
            | project changeTime, resourceId, resourceGroup, changeType,
                      summary = strcat(tostring(properties.changeType), ' — ',
                                       tostring(split(resourceId, '/')[8]))
            | order by changeTime desc
            | take {MaxRows}
            """;

        logger.LogInformation(
            "deployment_diff invoked | subscription={SubscriptionId} | resourceGroup={ResourceGroup} | lookbackMinutes={Lookback}",
            subscriptionId, resourceGroup ?? "(all)", lookbackMinutes);

        // ── Execute Resource Graph query ─────────────────────────────────────
        try
        {
            var queryContent = new ResourceQueryContent(query);
            queryContent.Subscriptions.Add(subscriptionId);

            var tenantCollection = armClient.GetTenants();
            TenantResource? firstTenant = null;
            await foreach (var t in tenantCollection.GetAllAsync(cancellationToken: cancellationToken))
            {
                firstTenant = t;
                break;
            }
            if (firstTenant is null)
                throw new InvalidOperationException("No Azure tenant accessible via ArmClient.");
            var graphResponse  = await firstTenant
                .GetResourcesAsync(queryContent, cancellationToken);

            var resultJson = graphResponse.Value.Data.ToString();

            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            var changes = ParseChanges(root);

            logger.LogInformation(
                "deployment_diff completed | subscription={SubscriptionId} | changeCount={Count}",
                subscriptionId, changes.Length);

            var envelope = new
            {
                ok             = true,
                subscriptionId,
                lookbackMinutes,
                changeCount    = changes.Length,
                changes,
                executedAtUtc,
                error          = (object?)null
            };

            return JsonSerializer.Serialize(envelope, JsonOpts);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("deployment_diff cancelled | subscription={SubscriptionId}", subscriptionId);
            return Fail(subscriptionId, executedAtUtc, "Query was cancelled.", "OperationCancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "deployment_diff failed | subscription={SubscriptionId}", subscriptionId);

            var message = hostEnvironment.IsDevelopment()
                ? "Local auth failed. Ensure 'az login --tenant <tid>' is current and the " +
                  "account has 'Reader' on the subscription and 'Microsoft.ResourceGraph/resources/read'. " +
                  "See docs/local-dev-auth.md for details. " +
                  $"[{ex.GetType().Name}] {ex.Message}"
                : ex.Message;

            return Fail(subscriptionId, executedAtUtc, message, ex.GetType().Name);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the Resource Graph tabular result (columns + rows) into a flat change array.
    /// The query projects: changeTime, resourceId, resourceGroup, changeType, summary.
    /// </summary>
    private static object[] ParseChanges(JsonElement root)
    {
        // The result is either the raw table object or nested under a "data" key
        // depending on Resource Graph SDK version.
        JsonElement tableRoot = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("rows", out _)
            ? root
            : root.TryGetProperty("data", out var d) ? d : root;

        if (!tableRoot.TryGetProperty("rows", out var rowsEl) ||
            !tableRoot.TryGetProperty("columns", out var colsEl))
        {
            return [];
        }

        // Build column-name index
        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var col in colsEl.EnumerateArray())
        {
            if (col.TryGetProperty("name", out var nameProp))
                cols[nameProp.GetString() ?? $"col{i}"] = i;
            i++;
        }

        var changes = new List<object>();
        foreach (var row in rowsEl.EnumerateArray())
        {
            var cells = row.EnumerateArray().ToArray();

            string Get(string name) =>
                cols.TryGetValue(name, out var idx) && idx < cells.Length
                    ? cells[idx].ToString()
                    : string.Empty;

            changes.Add(new
            {
                changeTime     = Get("changeTime"),
                resourceId     = Get("resourceId"),
                resourceGroup  = Get("resourceGroup"),
                changeType     = Get("changeType"),
                summary        = Get("summary"),
            });
        }

        return changes.ToArray();
    }

    /// <summary>
    /// Escapes a string for safe embedding in a KQL string literal (single-quoted).
    /// Replaces single quotes with escaped form to prevent KQL injection.
    /// </summary>
    private static string EscapeKqlString(string value) =>
        value.Replace("'", "\\'");

    private static string Fail(string subscriptionId, DateTimeOffset executedAtUtc,
        string message, string errorType)
    {
        var envelope = new
        {
            ok             = false,
            subscriptionId,
            lookbackMinutes = 0,
            changeCount    = 0,
            changes        = Array.Empty<object>(),
            executedAtUtc,
            error          = new { message, type = errorType }
        };

        return JsonSerializer.Serialize(envelope, JsonOpts);
    }
}
