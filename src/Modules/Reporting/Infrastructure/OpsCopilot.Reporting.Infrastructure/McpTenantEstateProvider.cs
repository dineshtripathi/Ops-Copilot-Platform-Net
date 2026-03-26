using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.McpClient;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Retrieves the tenant's Azure subscription estate via the McpHost
/// <c>list_subscriptions</c> tool.
///
/// Replaces <see cref="AzureTenantEstateProvider"/> to remove the direct
/// Azure.ResourceManager dependency from the Reporting.Infrastructure module boundary.
/// </summary>
internal sealed class McpTenantEstateProvider(
    ReportingMcpHostClient              mcp,
    ILogger<McpTenantEstateProvider>    logger) : ITenantEstateProvider
{
    public async Task<TenantEstateSummary?> GetTenantEstateSummaryAsync(
        string tenantId,
        CancellationToken ct)
    {
        var json = await mcp.CallToolAsync(
            "list_subscriptions",
            new Dictionary<string, object?> { ["tenantId"] = tenantId },
            ct);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "list_subscriptions returned non-JSON for tenant {Tenant}.", tenantId);
            return null;
        }

        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var err = root.TryGetProperty("error", out var ep)
                ? (ep.ValueKind == JsonValueKind.String ? ep.GetString() ?? "(no error message)" : ep.GetRawText())
                : "(no error message)";
            logger.LogWarning("list_subscriptions failed for tenant {Tenant}: {Error}", tenantId, err);
            return null;
        }

        var accessible  = root.TryGetProperty("accessible", out var a) ? a.GetInt32() : 0;
        var active      = root.TryGetProperty("active",     out var v) ? v.GetInt32() : 0;
        var diagnostic  = root.TryGetProperty("diagnostic", out var d) && d.ValueKind == JsonValueKind.String
                              ? d.GetString()
                              : null;

        var subscriptions = new List<AzureSubscriptionSummary>();

        if (root.TryGetProperty("subscriptions", out var subs)
            && subs.ValueKind == JsonValueKind.Array)
        {
            foreach (var sub in subs.EnumerateArray())
            {
                var subId       = sub.TryGetProperty("subscriptionId", out var si) ? si.GetString() ?? "" : "";
                var displayName = sub.TryGetProperty("displayName",    out var dn) ? dn.GetString() ?? "" : "";
                var state       = sub.TryGetProperty("state",          out var st) ? st.GetString() ?? "" : "";
                subscriptions.Add(new AzureSubscriptionSummary(subId, displayName, state));
            }
        }

        return new TenantEstateSummary(tenantId, accessible, active, subscriptions, diagnostic);
    }
}
