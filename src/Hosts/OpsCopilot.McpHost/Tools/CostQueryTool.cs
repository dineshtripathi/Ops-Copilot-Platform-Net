using System.ComponentModel;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.CostManagement;
using Azure.ResourceManager.CostManagement.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

[McpServerToolType]
public sealed class CostQueryTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private const int MaxLookbackDays = 90;

    [McpServerTool(Name = "cost_query")]
    [Description(
        "Queries Azure Cost Management for actual pre-tax costs in a subscription " +
        "or resource group, grouped by service name. Returns a ranked list of " +
        "services by cost for the requested look-back window.")]
    public static async Task<string> ExecuteAsync(
        // DI-injected — NOT part of the MCP JSON schema:
        ArmClient armClient,
        ILoggerFactory loggerFactory,
        // MCP parameters — appear in the tool's JSON schema:
        [Description("Azure subscription ID (GUID).")] string subscriptionId,
        [Description("Optional resource group name to narrow the scope to a single RG. " +
                     "Omit to query the whole subscription.")] string? resourceGroupName = null,
        [Description("Number of days of cost history to retrieve (1–90). Defaults to 30.")] int lookbackDays = 30,
        CancellationToken cancellationToken = default)
    {
        var log = loggerFactory.CreateLogger<CostQueryTool>();

        // ── Input validation ──────────────────────────────────────────────────
        if (!Guid.TryParse(subscriptionId, out _))
            return Fail("subscriptionId must be a valid GUID.");

        if (lookbackDays < 1 || lookbackDays > MaxLookbackDays)
            return Fail($"lookbackDays must be between 1 and {MaxLookbackDays}.");

        // ── Build scope ───────────────────────────────────────────────────────
        var scopePath = string.IsNullOrWhiteSpace(resourceGroupName)
            ? $"/subscriptions/{subscriptionId}"
            : $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}";

        var scope = new ResourceIdentifier(scopePath);

        // ── Build query ───────────────────────────────────────────────────────
        var to   = DateTimeOffset.UtcNow;
        var from = to.AddDays(-lookbackDays);

        // QueryDataset constructor initialises Aggregation and Grouping collections.
        var dataset = new QueryDataset();
        dataset.Aggregation["PreTaxCost"] = new QueryAggregation("PreTaxCost", FunctionType.Sum);
        dataset.Grouping.Add(new QueryGrouping(QueryColumnType.Dimension, "ServiceName"));
        // Granularity is left null — returns aggregated totals (not daily breakdown).

        var queryDef = new QueryDefinition(ExportType.ActualCost, TimeframeType.Custom, dataset)
        {
            TimePeriod = new QueryTimePeriod(from, to)
        };

        log.LogInformation(
            "cost_query scope={Scope} from={From:yyyy-MM-dd} to={To:yyyy-MM-dd}",
            scopePath, from, to);

        // ── Execute ───────────────────────────────────────────────────────────
        try
        {
            var response = await CostManagementExtensions.UsageQueryAsync(
                armClient, scope, queryDef, cancellationToken);

            var result = response.Value;

            // ── Map columns by name (order is not guaranteed) ─────────────────────
            var columns = result.Columns;
            int costIdx = FindColumn(columns, "PreTaxCost");
            int svcIdx  = FindColumn(columns, "ServiceName");
            int curIdx  = FindColumn(columns, "Currency");

            if (costIdx < 0 || svcIdx < 0)
            {
                var colNames = string.Join(", ", columns.Select(c => c.Name));
                log.LogWarning("cost_query: unexpected column set — {Columns}", colNames);
                return Fail($"Unexpected response columns: {colNames}");
            }

            // ── Build output rows ─────────────────────────────────────────────────
            var rows = new List<CostRow>();
            double totalCost = 0.0;
            string currency  = "USD";

            foreach (var rawRow in result.Rows)
            {
                // Each rawRow is IList<BinaryData> — one element per column.
                double cost = rawRow[costIdx].ToObjectFromJson<double>();
                string svc  = rawRow[svcIdx].ToObjectFromJson<string?>() ?? "Unknown";

                if (curIdx >= 0)
                    currency = rawRow[curIdx].ToObjectFromJson<string?>() ?? currency;

                totalCost += cost;
                rows.Add(new CostRow(svc, Math.Round(cost, 4)));
            }

            // Sort descending by cost for readability.
            rows.Sort((a, b) => b.Cost.CompareTo(a.Cost));

            return JsonSerializer.Serialize(new
            {
                ok               = true,
                subscriptionId,
                resourceGroupName,
                scope            = scopePath,
                from             = from.ToString("O"),
                to               = to.ToString("O"),
                currency,
                totalCost        = Math.Round(totalCost, 4),
                rowCount         = rows.Count,
                rows,
                error            = (string?)null,
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "cost_query failed | scope={Scope}", scopePath);
            return Fail(ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Fail(string message) =>
        JsonSerializer.Serialize(new { ok = false, error = message }, JsonOpts);

    private static int FindColumn(IReadOnlyList<QueryColumn> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    // Private record used only for output serialisation.
    private sealed record CostRow(string ServiceName, double Cost);
}
