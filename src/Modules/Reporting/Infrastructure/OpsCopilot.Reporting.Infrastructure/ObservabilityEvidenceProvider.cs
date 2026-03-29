using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

internal sealed class ObservabilityEvidenceProvider(
    IPackEvidenceExecutor packEvidenceExecutor,
    IConfiguration configuration,
    ILogger<ObservabilityEvidenceProvider> logger) : IObservabilityEvidenceProvider
{
    private const string AppInsightsPackName = "app-insights";
    private const string LiveBlastRadiusCollectorId = "live-blast-radius";
    private const string LivePolicyActivityCollectorId = "live-policy-activity";
    private static readonly HashSet<string> ObservabilityCollectors =
    [
        "top-exceptions",
        "failed-requests",
        "failed-dependencies",
        "timeout-patterns",
        "error-trends",
        "http-status-distribution",
        "availability-signals"
    ];

    private static readonly IReadOnlyDictionary<string, string> CollectorRunbookMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["top-exceptions"]      = "/runbooks/exception-diagnosis.md",
            ["failed-requests"]     = "/runbooks/exception-diagnosis.md",
            ["failed-dependencies"] = "/runbooks/dependency-failure-diagnosis.md",
            ["timeout-patterns"]    = "/runbooks/dependency-failure-diagnosis.md",
        };

    private static readonly JsonDocumentOptions JsonOpts = new() { AllowTrailingCommas = true };

    public async Task<ObservabilityEvidenceSummary?> GetSummaryAsync(
        Guid runId,
        string tenantId,
        string? workspaceId,
        CancellationToken ct)
        => await ExecuteSummaryAsync(runId, tenantId, ct, includeEmptyForLive: false);

    public async Task<ObservabilityEvidenceSummary?> GetLiveSummaryAsync(
        string tenantId,
        CancellationToken ct)
        => await ExecuteSummaryAsync(Guid.NewGuid(), tenantId, ct, includeEmptyForLive: true);

    public async Task<LiveImpactEvidenceSummary?> GetLiveImpactSummaryAsync(
        string tenantId,
        CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        try
        {
            var result = await packEvidenceExecutor.ExecuteAsync(
                new PackEvidenceExecutionRequest("B", tenantId, runId.ToString("N")),
                ct);

            return MapLiveImpact(result.EvidenceItems);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Live impact evidence collection failed for run {RunId}", runId);
            return null;
        }
    }

    public async Task<(ObservabilityEvidenceSummary? Observability, LiveImpactEvidenceSummary? Impact)> GetLiveCombinedAsync(
        string tenantId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        try
        {
            var result = await packEvidenceExecutor.ExecuteAsync(
                new PackEvidenceExecutionRequest("B", tenantId, runId.ToString("N"), fromUtc, toUtc),
                ct);

            var observability = MapObservabilitySummaryFromItems(result.EvidenceItems);
            var impact        = MapLiveImpact(result.EvidenceItems);

            if (impact is { CoverageStatus: "live-data-no-impact" })
            {
                var workspaceId =
                    configuration[$"Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId"]
                    ?? configuration["Observability:LogAnalyticsWorkspaceId"]
                    ?? configuration["WORKSPACE_ID"]
                    ?? "(not configured)";

                impact = impact with
                {
                    Diagnostic = $"Live impact collectors returned zero rows. " +
                                 $"Queried workspace: {workspaceId}. " +
                                 $"Verify AzureActivity logs are connected to this workspace or widen the incident time window."
                };
            }

            return (observability, impact);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Live combined evidence collection failed for run {RunId}", runId);
            return (null, null);
        }
    }

    private static ObservabilityEvidenceSummary MapObservabilitySummaryFromItems(
        IReadOnlyList<PackEvidenceItem> items)
    {
        var collectors = items
            .Where(IsObservabilityCollector)
            .Select(MapCollector)
            .ToList();

        if (collectors.Count == 0)
        {
            return new ObservabilityEvidenceSummary(
                Source: AppInsightsPackName,
                CollectorCount: 0,
                SuccessfulCollectors: 0,
                FailedCollectors: 0,
                CollectorSummaries: [],
                Diagnostic: "Live observability collectors were not returned by governed pack execution.",
                CoverageStatus: "collectors-missing",
                IsActionable: false,
                Recommendations:
                [
                    "Verify app-insights pack deployment and collector registration.",
                    "Confirm live collector IDs are available in the current environment."
                ]);
        }

        var successfulCollectors = collectors.Count(c => string.Equals(c.Status, "Ready", StringComparison.OrdinalIgnoreCase));
        var failedCollectors     = collectors.Count(c => !string.Equals(c.Status, "Ready", StringComparison.OrdinalIgnoreCase));
        var hasRows              = collectors.Any(c => c.RowCount > 0);
        var isActionable         = hasRows || failedCollectors > 0;
        var coverageStatus       = failedCollectors > 0
            ? "collectors-unavailable"
            : hasRows
                ? "live-signal-detected"
                : "live-data-no-impact";
        var diagnostic           = failedCollectors > 0
            ? "One or more live observability collectors failed."
            : hasRows
                ? null
                : "Live observability collectors succeeded but returned zero rows for the current workspace/time window.";

        return new ObservabilityEvidenceSummary(
            Source: AppInsightsPackName,
            CollectorCount: collectors.Count,
            SuccessfulCollectors: successfulCollectors,
            FailedCollectors: failedCollectors,
            CollectorSummaries: collectors,
            Diagnostic: diagnostic,
            CoverageStatus: coverageStatus,
            IsActionable: isActionable,
            Recommendations: BuildObservabilityRecommendations(coverageStatus),
            FailurePattern: DeriveFailurePattern(collectors),
            OwnerPath: DeriveOwnerPath(collectors));
    }

    private async Task<ObservabilityEvidenceSummary?> ExecuteSummaryAsync(
        Guid runId,
        string tenantId,
        CancellationToken ct,
        bool includeEmptyForLive)
    {
        try
        {
            var result = await packEvidenceExecutor.ExecuteAsync(
                new PackEvidenceExecutionRequest("B", tenantId, runId.ToString("N")),
                ct);

            var collectors = result.EvidenceItems
                .Where(IsObservabilityCollector)
                .Select(MapCollector)
                .ToList();

            if (collectors.Count == 0)
            {
                if (!includeEmptyForLive)
                    return null;

                return new ObservabilityEvidenceSummary(
                    Source: AppInsightsPackName,
                    CollectorCount: 0,
                    SuccessfulCollectors: 0,
                    FailedCollectors: 0,
                    CollectorSummaries: [],
                    Diagnostic: "Live observability collectors were not returned by governed pack execution.",
                    CoverageStatus: "collectors-missing",
                    IsActionable: false,
                    Recommendations:
                    [
                        "Verify app-insights pack deployment and collector registration.",
                        "Confirm live collector IDs are available in the current environment."
                    ]);
            }

            var successfulCollectors = collectors.Count(c => string.Equals(c.Status, "Ready", StringComparison.OrdinalIgnoreCase));
            var failedCollectors = collectors.Count(c => !string.Equals(c.Status, "Ready", StringComparison.OrdinalIgnoreCase));
            var hasRows = collectors.Any(c => c.RowCount > 0);
            var isActionable = hasRows || failedCollectors > 0;
            var coverageStatus = failedCollectors > 0
                ? "collectors-unavailable"
                : hasRows
                    ? "live-signal-detected"
                    : "live-data-no-impact";
            var diagnostic = failedCollectors > 0
                ? "One or more live observability collectors failed."
                : hasRows
                    ? null
                    : "Live observability collectors succeeded but returned zero rows for the current workspace/time window.";
            var recommendations = BuildObservabilityRecommendations(coverageStatus);

            return new ObservabilityEvidenceSummary(
                Source: AppInsightsPackName,
                CollectorCount: collectors.Count,
                SuccessfulCollectors: successfulCollectors,
                FailedCollectors: failedCollectors,
                CollectorSummaries: collectors,
                Diagnostic: diagnostic,
                CoverageStatus: coverageStatus,
                IsActionable: isActionable,
                Recommendations: recommendations,
                FailurePattern: DeriveFailurePattern(collectors),
                OwnerPath: DeriveOwnerPath(collectors));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Observability evidence collection failed for run {RunId}", runId);
            if (includeEmptyForLive)
            {
                return new ObservabilityEvidenceSummary(
                    Source: AppInsightsPackName,
                    CollectorCount: 0,
                    SuccessfulCollectors: 0,
                    FailedCollectors: 1,
                    CollectorSummaries: [],
                    Diagnostic: "Live observability collector execution failed.",
                    CoverageStatus: "collectors-unavailable",
                    IsActionable: false,
                    Recommendations:
                    [
                        "Verify workspace access and collector permissions before triage.",
                        "Retry live diagnostics once collector failures are resolved."
                    ]);
            }

            return null;
        }
    }

    private static IReadOnlyList<string> BuildObservabilityRecommendations(string coverageStatus)
        => coverageStatus switch
        {
            "live-signal-detected" =>
            [
                "Prioritize collector rows with highest failure/exception counts.",
                "Correlate failed requests/dependencies with impacted resources and recent degraded runs.",
                "Use top exception and timeout patterns to select first remediation path."
            ],
            "live-data-no-impact" =>
            [
                "No active observability failures detected; validate incident scope beyond application telemetry.",
                "Widen lookback window or verify incident timestamp alignment.",
                "Confirm workspace mapping if external alerts indicate active faults."
            ],
            "collectors-unavailable" =>
            [
                "One or more collectors are unavailable; inspect collector errors and workspace RBAC.",
                "Retry live evidence after telemetry access issues are addressed."
            ],
            _ =>
            [
                "Validate live collector registration and pack deployment for this environment."
            ]
        };

    private static bool IsObservabilityCollector(PackEvidenceItem item)
        => string.Equals(item.PackName, AppInsightsPackName, StringComparison.OrdinalIgnoreCase)
           && ObservabilityCollectors.Contains(item.CollectorId);

    private static LiveImpactEvidenceSummary? MapLiveImpact(IReadOnlyList<PackEvidenceItem> items)
    {
        var blastItem = items.FirstOrDefault(i =>
            string.Equals(i.PackName, AppInsightsPackName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.CollectorId, LiveBlastRadiusCollectorId, StringComparison.OrdinalIgnoreCase));

        var activityItem = items.FirstOrDefault(i =>
            string.Equals(i.PackName, AppInsightsPackName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.CollectorId, LivePolicyActivityCollectorId, StringComparison.OrdinalIgnoreCase));

        var blast = MapBlastRadius(blastItem);
        var activity = MapActivitySignals(activityItem);

        var diagnostic = BuildLiveImpactDiagnostic(blastItem, activityItem, blast, activity);
        var successfulCollectors = (blastItem is not null && string.IsNullOrWhiteSpace(blastItem.ErrorMessage) ? 1 : 0)
                                   + (activityItem is not null && string.IsNullOrWhiteSpace(activityItem.ErrorMessage) ? 1 : 0);
        var failedCollectors = (blastItem is not null && !string.IsNullOrWhiteSpace(blastItem.ErrorMessage) ? 1 : 0)
                               + (activityItem is not null && !string.IsNullOrWhiteSpace(activityItem.ErrorMessage) ? 1 : 0);
        var isActionable = IsActionableLiveImpact(blast, activity);
        var coverageStatus = ResolveCoverageStatus(blastItem, activityItem, blast, activity, failedCollectors, isActionable);
        var recommendations = BuildRecommendations(coverageStatus, blastItem, activityItem);

        return new LiveImpactEvidenceSummary(
            Source: AppInsightsPackName,
            BlastRadius: blast,
            ActivitySignals: activity,
            Diagnostic: diagnostic,
            CoverageStatus: coverageStatus,
            IsActionable: isActionable,
            SuccessfulCollectors: successfulCollectors,
            FailedCollectors: failedCollectors,
            Recommendations: recommendations);
    }

    private static bool IsActionableLiveImpact(
        BlastRadiusSummary? blast,
        ActivitySignalSummary? activity)
        => (blast?.ImpactedResources ?? 0) > 0
           || (blast?.ImpactedApplications ?? 0) > 0
           || (activity?.PolicyDenials ?? 0) > 0
           || (activity?.ScopeDenials ?? 0) > 0
           || (activity?.BudgetDenials ?? 0) > 0
           || (activity?.DegradedModeEvents ?? 0) > 0;

    private static string ResolveCoverageStatus(
        PackEvidenceItem? blastItem,
        PackEvidenceItem? activityItem,
        BlastRadiusSummary? blast,
        ActivitySignalSummary? activity,
        int failedCollectors,
        bool isActionable)
    {
        if (blastItem is null && activityItem is null)
            return "collectors-missing";

        if (failedCollectors > 0)
            return "collectors-unavailable";

        if (isActionable)
            return "live-impact-detected";

        if (blastItem is not null && activityItem is not null)
            return "live-data-no-impact";

        if (blast is not null || activity is not null)
            return "live-data-no-impact";

        return "live-data-unavailable";
    }

    private static IReadOnlyList<string> BuildRecommendations(
        string coverageStatus,
        PackEvidenceItem? blastItem,
        PackEvidenceItem? activityItem)
        => coverageStatus switch
        {
            "live-impact-detected" =>
            [
                "Prioritize resources in the live impact section for immediate triage.",
                "Correlate impacted resources with the most recent degraded/failed runs.",
                "Use live App Insights collector highlights to identify failure pattern and owner path."
            ],
            "live-data-no-impact" =>
            [
                "No current blast/policy impact was detected; validate whether the incident is application-only.",
                "Widen lookback window or confirm the active incident timestamp/window.",
                "If incident is active, verify tenant workspace mapping to the incident telemetry workspace."
            ],
            "collectors-unavailable" =>
            [
                "One or more live impact collectors failed; inspect collector error details.",
                "Confirm AzureActivity/AzureDiagnostics availability and RBAC for the workspace.",
                "Retry triage after telemetry access issues are resolved."
            ],
            "collectors-missing" =>
            [
                "Live impact collectors were not emitted by pack execution; verify app-insights pack version/deployment.",
                "Confirm `live-blast-radius` and `live-policy-activity` collectors are present in pack manifest."
            ],
            _ =>
            [
                "Live impact coverage is limited; verify workspace mapping and incident lookback settings.",
                "Check collector payloads for parse/schema mismatches."
            ]
        };

    private static string? BuildLiveImpactDiagnostic(
        PackEvidenceItem? blastItem,
        PackEvidenceItem? activityItem,
        BlastRadiusSummary? blast,
        ActivitySignalSummary? activity)
    {
        if (blast is not null || activity is not null)
            return null;

        if (blastItem is null && activityItem is null)
            return "Live impact collectors were not returned by governed pack execution.";

        var errors = new[] { blastItem?.ErrorMessage, activityItem?.ErrorMessage }
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Cast<string>()
            .ToArray();

        if (errors.Length > 0)
            return $"Live impact collectors failed: {string.Join(" | ", errors)}";

        var blastRows = blastItem?.RowCount ?? 0;
        var activityRows = activityItem?.RowCount ?? 0;
        if (blastRows == 0 && activityRows == 0)
            return "Live impact collectors succeeded but returned zero rows for the current workspace/time window.";

        return "Live impact signals are unavailable for the current workspace mapping.";
    }

    private static BlastRadiusSummary? MapBlastRadius(PackEvidenceItem? item)
    {
        if (item is null || !string.IsNullOrWhiteSpace(item.ErrorMessage) || string.IsNullOrWhiteSpace(item.ResultJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(item.ResultJson, JsonOpts);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var row = doc.RootElement[0];
            return new BlastRadiusSummary(
                ImpactedSubscriptions: GetInt(row, "impactedSubscriptions"),
                ImpactedResourceGroups: GetInt(row, "impactedResourceGroups"),
                ImpactedResources: GetInt(row, "impactedResources"),
                ImpactedApplications: GetInt(row, "impactedApplications"));
        }
        catch
        {
            return null;
        }
    }

    private static ActivitySignalSummary? MapActivitySignals(PackEvidenceItem? item)
    {
        if (item is null || !string.IsNullOrWhiteSpace(item.ErrorMessage) || string.IsNullOrWhiteSpace(item.ResultJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(item.ResultJson, JsonOpts);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var row = doc.RootElement[0];
            return new ActivitySignalSummary(
                TotalPolicyEvents: GetInt(row, "TotalPolicyEvents"),
                PolicyDenials: GetInt(row, "PolicyDenials"),
                ScopeDenials: GetInt(row, "ScopeDenials"),
                BudgetDenials: GetInt(row, "BudgetDenials"),
                DegradedModeEvents: GetInt(row, "DegradedModeEvents"));
        }
        catch
        {
            return null;
        }
    }

    private static ObservabilityEvidenceCollectorSummary MapCollector(PackEvidenceItem item)
    {
        var highlights = ExtractHighlights(item.ResultJson, item.RowCount);
        var status = string.IsNullOrWhiteSpace(item.ErrorMessage) ? "Ready" : "Unavailable";

        return new ObservabilityEvidenceCollectorSummary(
            CollectorId: item.CollectorId,
            Title: MapTitle(item.CollectorId),
            RowCount: item.RowCount,
            Status: status,
            Highlights: highlights,
            ErrorMessage: item.ErrorMessage,
            RunbookRef: CollectorRunbookMap.GetValueOrDefault(item.CollectorId));
    }

    private static string MapTitle(string collectorId) => collectorId switch
    {
        "top-exceptions" => "Top Exceptions",
        "failed-requests" => "Failed Requests",
        "failed-dependencies" => "Failed Dependencies",
        "timeout-patterns" => "Timeout Patterns",
        "error-trends" => "Error Trends",
        "http-status-distribution" => "HTTP Status Distribution",
        "availability-signals" => "Availability Signals",
        _ => collectorId
    };

    private static IReadOnlyList<string> ExtractHighlights(string? resultJson, int rowCount)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            return rowCount > 0 ? [$"Returned {rowCount} rows"] : [];

        try
        {
            using var doc = JsonDocument.Parse(resultJson, JsonOpts);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return rowCount > 0 ? [$"Returned {rowCount} rows"] : [];

            var highlights = doc.RootElement
                .EnumerateArray()
                .Take(3)
                .Select(SummarizeRow)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();

            if (highlights.Count == 0 && rowCount > 0)
                highlights.Add($"Returned {rowCount} rows");

            return highlights;
        }
        catch
        {
            return rowCount > 0 ? [$"Returned {rowCount} rows"] : [];
        }
    }

    private static string? SummarizeRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
            return Truncate(row.ToString());

        if (TryGetString(row, "exceptionType", out var exceptionType))
        {
            var message = GetFirstString(row, "SampleMessage", "outerMessage", "message");
            var count = GetScalar(row, "Count");
            return Truncate($"{exceptionType}{FormatCount(count)}{FormatDetail(message)}");
        }

        if (TryGetString(row, "resultCode", out var resultCode))
        {
            var displayCode = resultCode == "0" ? "No response" : resultCode;
            var url = GetFirstString(row, "url", "target", "name");
            var count = GetScalar(row, "Count");
            return Truncate($"{displayCode}{FormatCount(count)}{FormatDetail(url)}");
        }

        if (TryGetString(row, "target", out var target))
        {
            var type = GetFirstString(row, "type");
            var count = GetScalar(row, "Count");
            return Truncate($"{type ?? "Dependency"}: {target}{FormatCount(count)}");
        }

        if (row.TryGetProperty("AvailabilityPercent", out var availability))
        {
            var p99 = GetScalar(row, "P99ResponseTime");
            return Truncate($"Availability {availability.ToString()}%{FormatDetail(p99 is null ? null : $"P99 {p99}ms")}");
        }

        if (row.TryGetProperty("ErrorRate", out var errorRate))
        {
            var failed = GetScalar(row, "FailedCount");
            return Truncate($"Error rate {errorRate.ToString()}%{FormatDetail(failed is null ? null : $"Failed {failed}")}");
        }

        var pairs = row.EnumerateObject()
            .Where(p => IsScalar(p.Value))
            .Take(3)
            .Select(p => $"{p.Name}: {p.Value}")
            .ToList();

        return pairs.Count > 0 ? Truncate(string.Join(" | ", pairs)) : null;
    }

    private static bool IsScalar(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static bool TryGetString(JsonElement row, string name, out string? value)
    {
        value = null;
        if (!row.TryGetProperty(name, out var prop))
            return false;

        value = prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetFirstString(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetString(row, name, out var value))
                return value;
        }

        return null;
    }

    private static string? GetScalar(JsonElement row, string name)
        => row.TryGetProperty(name, out var prop) && IsScalar(prop) ? prop.ToString() : null;

    private static int GetInt(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var prop))
            return 0;

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(prop.GetString(), out var i) => i,
            _ => 0
        };
    }

    private static string FormatCount(string? count)
        => string.IsNullOrWhiteSpace(count) ? string.Empty : $" ({count})";

    private static string FormatDetail(string? detail)
        => string.IsNullOrWhiteSpace(detail) ? string.Empty : $" - {detail}";

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= 200 ? value : value[..197] + "...";
    }

    private static string? DeriveFailurePattern(IReadOnlyList<ObservabilityEvidenceCollectorSummary> collectors)
    {
        var highlights = collectors
            .SelectMany(c => c.Highlights)
            .ToList();

        var patterns = new List<string>();

        if (highlights.Any(h => h.Contains("403", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("401", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)))
            patterns.Add("Auth rejection");

        if (highlights.Any(h => h.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("408", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("RequestTimeout", StringComparison.OrdinalIgnoreCase)))
            patterns.Add("Timeout");

        if (highlights.Any(h => h.Contains("500", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("502", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("503", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("InternalServerError", StringComparison.OrdinalIgnoreCase)))
            patterns.Add("Server error");

        if (highlights.Any(h => h.Contains("404", StringComparison.OrdinalIgnoreCase)
                                || h.Contains("NotFound", StringComparison.OrdinalIgnoreCase)))
            patterns.Add("Not found");

        return patterns.Count > 0 ? string.Join(", ", patterns) : null;
    }

    private static string? DeriveOwnerPath(IReadOnlyList<ObservabilityEvidenceCollectorSummary> collectors)
    {
        var depCollector = collectors.FirstOrDefault(c =>
            string.Equals(c.CollectorId, "failed-dependencies", StringComparison.OrdinalIgnoreCase));

        if (depCollector is null || depCollector.Highlights.Count == 0)
            return null;

        var ownerGroups = depCollector.Highlights
            .Select(ExtractDependencyTarget)
            .Where(t => t is not null)
            .Select(t => ClassifyOwner(t!))
            .GroupBy(o => o)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        return ownerGroups.Count > 0 ? string.Join(" → ", ownerGroups) : null;
    }

    private static string? ExtractDependencyTarget(string highlight)
    {
        var urlMatch = Regex.Match(highlight, @"https?://([^/\s(]+)");
        if (urlMatch.Success)
            return urlMatch.Groups[1].Value;

        var typeMatch = Regex.Match(highlight, @"[\w]+:\s+([^\s(]+)");
        return typeMatch.Success ? typeMatch.Groups[1].Value : null;
    }

    private static string ClassifyOwner(string target)
    {
        if (target.Contains(".azure.com", StringComparison.OrdinalIgnoreCase)
            || target.Contains(".database.windows.net", StringComparison.OrdinalIgnoreCase)
            || target.Contains(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase)
            || target.Contains(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
            return "Azure infrastructure";

        if (target.Contains(".sentry.io", StringComparison.OrdinalIgnoreCase)
            || target.Contains(".datadoghq.com", StringComparison.OrdinalIgnoreCase)
            || target.Contains(".newrelic.com", StringComparison.OrdinalIgnoreCase))
            return "Monitoring vendor";

        if (target.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(target, @"^10\.|\.internal$|\.svc\.cluster\.local"))
            return "Internal service";

        return "External vendor";
    }
}