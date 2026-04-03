using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.McpClient;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Retrieves the tenant's Azure resource inventory (resource groups, App Insights components,
/// and Log Analytics workspaces) via the McpHost resource-graph tools.
/// </summary>
internal sealed class McpTenantResourceInventoryProvider(
    IReportingMcpHostClient                         mcp,
    ILogger<McpTenantResourceInventoryProvider>     logger) : ITenantResourceInventoryProvider
{
    public async Task<TenantResourceInventory?> GetInventoryAsync(
        string tenantId,
        CancellationToken ct)
    {
        // Step 1: resolve subscription IDs for this tenant using list_subscriptions.
        var subscriptionIds = await GetSubscriptionIdsAsync(tenantId, ct);
        if (subscriptionIds is null)
            return null;

        var subArg = string.Join(",", subscriptionIds);

        // Step 2: run the three resource-graph queries in sequence
        //         (all share the same scoped McpHost client — sequential is safe).
        var resourceGroups          = await GetResourceGroupsAsync(subArg, ct);
        var appInsightsComponents   = await GetAppInsightsAsync(subArg, ct);
        var logAnalyticsWorkspaces  = await GetLogAnalyticsAsync(subArg, ct);

        return new TenantResourceInventory(
            TenantId:              tenantId,
            ResourceGroups:        resourceGroups,
            AppInsightsComponents: appInsightsComponents,
            LogAnalyticsWorkspaces: logAnalyticsWorkspaces);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<string>?> GetSubscriptionIdsAsync(
        string tenantId, CancellationToken ct)
    {
        var json = await mcp.CallToolAsync(
            "list_subscriptions",
            new Dictionary<string, object?> { ["tenantId"] = tenantId },
            ct);

        if (!TryParseOk(json, "list_subscriptions", tenantId, out var root))
            return null;

        var ids = new List<string>();

        if (root.TryGetProperty("subscriptions", out var subs) && subs.ValueKind == JsonValueKind.Array)
        {
            foreach (var sub in subs.EnumerateArray())
            {
                if (sub.TryGetProperty("subscriptionId", out var si) && si.GetString() is { Length: > 0 } id)
                    ids.Add(id);
            }
        }

        return ids.Count > 0 ? ids : null;
    }

    private async Task<IReadOnlyList<AzureResourceGroupSummary>> GetResourceGroupsAsync(
        string subscriptionIds, CancellationToken ct)
    {
        var json = await mcp.CallToolAsync(
            "list_resource_groups",
            new Dictionary<string, object?> { ["subscriptionIds"] = subscriptionIds },
            ct);

        if (!TryParseOk(json, "list_resource_groups", subscriptionIds, out var root))
            return [];

        var results = new List<AzureResourceGroupSummary>();

        if (root.TryGetProperty("resourceGroups", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                results.Add(new AzureResourceGroupSummary(
                    SubscriptionId:    GetStr(item, "subscriptionId"),
                    Name:              GetStr(item, "name"),
                    Location:          GetStr(item, "location"),
                    ProvisioningState: GetStr(item, "provisioningState")));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<AppInsightsComponentSummary>> GetAppInsightsAsync(
        string subscriptionIds, CancellationToken ct)
    {
        var json = await mcp.CallToolAsync(
            "list_app_insights",
            new Dictionary<string, object?> { ["subscriptionIds"] = subscriptionIds },
            ct);

        if (!TryParseOk(json, "list_app_insights", subscriptionIds, out var root))
            return [];

        var results = new List<AppInsightsComponentSummary>();

        if (root.TryGetProperty("components", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                results.Add(new AppInsightsComponentSummary(
                    SubscriptionId:      GetStr(item, "subscriptionId"),
                    ResourceGroup:       GetStr(item, "resourceGroup"),
                    Name:                GetStr(item, "name"),
                    Location:            GetStr(item, "location"),
                    Kind:                GetStr(item, "kind"),
                    WorkspaceResourceId: GetStrOrNull(item, "workspaceResourceId")));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<LogAnalyticsWorkspaceSummary>> GetLogAnalyticsAsync(
        string subscriptionIds, CancellationToken ct)
    {
        var json = await mcp.CallToolAsync(
            "list_log_analytics_workspaces",
            new Dictionary<string, object?> { ["subscriptionIds"] = subscriptionIds },
            ct);

        if (!TryParseOk(json, "list_log_analytics_workspaces", subscriptionIds, out var root))
            return [];

        var results = new List<LogAnalyticsWorkspaceSummary>();

        if (root.TryGetProperty("workspaces", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                _ = int.TryParse(GetStr(item, "retentionInDays"), out var retention);
                results.Add(new LogAnalyticsWorkspaceSummary(
                    SubscriptionId:  GetStr(item, "subscriptionId"),
                    ResourceGroup:   GetStr(item, "resourceGroup"),
                    Name:            GetStr(item, "name"),
                    Location:        GetStr(item, "location"),
                    CustomerId:      GetStrOrNull(item, "customerId"),
                    RetentionInDays: retention,
                    Sku:             GetStrOrNull(item, "sku")));
            }
        }

        return results;
    }

    private bool TryParseOk(string json, string toolName, string context, out JsonElement root)
    {
        root = default;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Tool} returned non-JSON (context={Context}).", toolName, context);
            return false;
        }

        root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var err = root.TryGetProperty("error", out var ep)
                ? (ep.ValueKind == JsonValueKind.String ? ep.GetString() ?? "(no error)" : ep.GetRawText())
                : "(no error)";
            logger.LogWarning("{Tool} returned ok=false (context={Context}): {Error}", toolName, context, err);
            return false;
        }

        return true;
    }

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
            ? p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : p.GetRawText()
            : string.Empty;

    private static string? GetStrOrNull(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
