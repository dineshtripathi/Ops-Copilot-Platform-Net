using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Mode-B+ evidence executor. Discovers packs whose <c>MinimumMode</c> is at or below
/// the deployment mode, reads their KQL query files, executes them via connectors,
/// and returns the results as evidence items.
/// </summary>
/// <remarks>
/// Triple-gated: execution requires
/// <list type="number">
///   <item><c>deploymentMode &gt;= B</c></item>
///   <item><c>Packs:EvidenceExecutionEnabled == true</c> in configuration</item>
///   <item>A valid, allowlisted workspace resolved by <see cref="ITenantWorkspaceResolver"/></item>
/// </list>
/// All operations are read-only; no mutations are performed.
/// </remarks>
// KQL-audit: safe — query text not logged
internal sealed class PackEvidenceExecutor : IPackEvidenceExecutor
{
    private const int DefaultMaxRows  = 50;
    private const int DefaultMaxChars = 4_000;

    private readonly IObservabilityQueryExecutor  _queryExecutor;
    private readonly IPackCatalog                 _catalog;
    private readonly IPackFileReader              _fileReader;
    private readonly ITenantWorkspaceResolver     _workspaceResolver;
    private readonly IConfiguration               _configuration;
    private readonly ILogger<PackEvidenceExecutor> _logger;
    private readonly IPacksTelemetry               _telemetry;

    public PackEvidenceExecutor(
        IObservabilityQueryExecutor   queryExecutor,
        IPackCatalog                  catalog,
        IPackFileReader               fileReader,
        ITenantWorkspaceResolver      workspaceResolver,
        IConfiguration                configuration,
        ILogger<PackEvidenceExecutor> logger,
        IPacksTelemetry               telemetry)
    {
        _queryExecutor     = queryExecutor     ?? throw new ArgumentNullException(nameof(queryExecutor));
        _catalog           = catalog           ?? throw new ArgumentNullException(nameof(catalog));
        _fileReader        = fileReader        ?? throw new ArgumentNullException(nameof(fileReader));
        _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
        _configuration     = configuration     ?? throw new ArgumentNullException(nameof(configuration));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
        _telemetry         = telemetry         ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public async Task<PackEvidenceExecutionResult> ExecuteAsync(
        PackEvidenceExecutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var tenantId      = request.TenantId ?? string.Empty;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"]      = tenantId,
            ["Mode"]          = request.DeploymentMode,
        });

        var evidenceItems = new List<PackEvidenceItem>();
        var errors        = new List<string>();

        // ── Gate 1: deployment mode ──────────────────────────────────────
        if (!IsModeEligible(request.DeploymentMode))
        {
            _logger.LogDebug(
                "Evidence execution skipped: deployment mode {Mode} is below B",
                request.DeploymentMode);
            _telemetry.RecordEvidenceSkipped(request.DeploymentMode, tenantId);
            return new PackEvidenceExecutionResult(evidenceItems, errors);
        }

        // ── Gate 2: feature flag ─────────────────────────────────────────
        if (!IsFeatureEnabled())
        {
            _logger.LogDebug("Evidence execution skipped: Packs:EvidenceExecutionEnabled is not true");
            _telemetry.RecordEvidenceSkipped(request.DeploymentMode, tenantId);
            return new PackEvidenceExecutionResult(evidenceItems, errors);
        }

        // ── Gate 3: workspace resolution ─────────────────────────────────
        var workspaceResult = await _workspaceResolver.ResolveAsync(tenantId, ct);
        if (!workspaceResult.Success)
        {
            _logger.LogWarning(
                "Evidence execution: workspace resolution failed for tenant {TenantId} — {ErrorCode}",
                tenantId, workspaceResult.ErrorCode);
            _telemetry.RecordWorkspaceResolutionFailed(tenantId, workspaceResult.ErrorCode!, correlationId);

            // Produce per-item errors for every eligible collector so callers
            // can see exactly which evidence was skipped and why.
            await AddWorkspaceErrorItemsAsync(
                request.DeploymentMode, workspaceResult.ErrorCode!,
                evidenceItems, errors, ct);

            return new PackEvidenceExecutionResult(evidenceItems, errors);
        }

        _telemetry.RecordEvidenceAttempt(request.DeploymentMode, tenantId, correlationId);

        var workspaceId             = workspaceResult.WorkspaceId!;
        var appInsightsResourcePath = workspaceResult.AppInsightsResourcePath;
        var maxRows                 = _configuration.GetValue("Packs:EvidenceMaxRows",  DefaultMaxRows);
        var maxChars                = _configuration.GetValue("Packs:EvidenceMaxChars", DefaultMaxChars);

        IReadOnlyList<LoadedPack> packs;
        try
        {
            packs = await _catalog.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query pack catalog for evidence execution");
            errors.Add($"Pack catalog query failed: {ex.Message}");
            return new PackEvidenceExecutionResult(evidenceItems, errors);
        }

        var eligiblePacks = packs.Where(p =>
            p.Validation.IsValid &&
            IsModeAtOrBelow(p.Manifest.MinimumMode, request.DeploymentMode));

        _logger.LogDebug(
            "Evidence execution: found {PackCount} eligible packs for mode {Mode}",
            eligiblePacks.Count(),
            request.DeploymentMode);

        foreach (var pack in eligiblePacks)
        {
            await ExecutePackCollectorsAsync(
                pack, request.DeploymentMode, workspaceId, appInsightsResourcePath, maxRows, maxChars,
                tenantId, correlationId, request.FromUtc, request.ToUtc, evidenceItems, errors, ct);
        }

        _logger.LogDebug(
            "Evidence execution complete: {ItemCount} items, {ErrorCount} errors",
            evidenceItems.Count,
            errors.Count);

        return new PackEvidenceExecutionResult(evidenceItems, errors);
    }

    // ── Workspace-resolution failure path ────────────────────────────────────
    // Iterates eligible packs/collectors and adds a per-item error for each so
    // callers can see exactly which evidence was skipped and why.
    private async Task AddWorkspaceErrorItemsAsync(
        string deploymentMode,
        string errorCode,
        List<PackEvidenceItem> evidenceItems,
        List<string> errors,
        CancellationToken ct)
    {
        IReadOnlyList<LoadedPack> packs;
        try
        {
            packs = await _catalog.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query pack catalog while building workspace error items");
            errors.Add($"workspace_{errorCode}: pack catalog unavailable");
            return;
        }

        const string connectorName = "azure-monitor";

        var errorMessage = errorCode == "missing_workspace"
            ? "Workspace not configured for tenant"
            : "Workspace not in allowlist";

        foreach (var pack in packs.Where(p =>
            p.Validation.IsValid &&
            IsModeAtOrBelow(p.Manifest.MinimumMode, deploymentMode)))
        {
            foreach (var ec in pack.Manifest.EvidenceCollectors
                .Where(ec => IsModeAtOrBelow(ec.RequiredMode, deploymentMode)))
            {
                errors.Add($"Pack '{pack.Manifest.Name}' collector '{ec.Id}': {errorCode}");
                evidenceItems.Add(new PackEvidenceItem(
                    pack.Manifest.Name, ec.Id, connectorName,
                    ec.QueryFile, null, null, 0, errorMessage));
            }
        }
    }

    private async Task ExecutePackCollectorsAsync(
        LoadedPack pack,
        string deploymentMode,
        string workspaceId,
        string? appInsightsResourcePath,
        int maxRows,
        int maxChars,
        string tenantId,
        string? correlationId,
        DateTime? fromUtc,
        DateTime? toUtc,
        List<PackEvidenceItem> evidenceItems,
        List<string> errors,
        CancellationToken ct)
    {
        var eligibleCollectors = pack.Manifest.EvidenceCollectors
            .Where(ec => IsModeAtOrBelow(ec.RequiredMode, deploymentMode));

        foreach (var ec in eligibleCollectors)
        {
            await ExecuteSingleCollectorAsync(
                pack, ec, workspaceId, appInsightsResourcePath, maxRows, maxChars,
                tenantId, correlationId, fromUtc, toUtc, evidenceItems, errors, ct);
        }
    }

    private async Task ExecuteSingleCollectorAsync(
        LoadedPack pack,
        EvidenceCollector ec,
        string workspaceId,
        string? appInsightsResourcePath,
        int maxRows,
        int maxChars,
        string tenantId,
        string? correlationId,
        DateTime? fromUtc,
        DateTime? toUtc,
        List<PackEvidenceItem> evidenceItems,
        List<string> errors,
        CancellationToken ct)
    {
        const string connectorName = "azure-monitor";
        var packName = pack.Manifest.Name;

        try
        {
            // Read the KQL query file — file PATH is logged, not KQL content
            string? queryContent = null;
            if (!string.IsNullOrWhiteSpace(ec.QueryFile))
            {
                queryContent = await _fileReader.ReadFileAsync(pack.PackPath, ec.QueryFile, ct);
                if (queryContent is null)
                {
                    _logger.LogWarning(
                        "Query file {QueryFile} not found for collector {CollectorId} in pack {PackId}",
                        ec.QueryFile, ec.Id, packName);
                    _telemetry.RecordQueryBlocked(packName, ec.Id, tenantId, correlationId);
                    errors.Add(
                        $"Pack '{packName}' collector '{ec.Id}': query file '{ec.QueryFile}' not found");
                    evidenceItems.Add(new PackEvidenceItem(
                        packName, ec.Id, connectorName,
                        ec.QueryFile, null, null, 0,
                        $"Query file '{ec.QueryFile}' not found"));
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(queryContent))
            {
                _logger.LogDebug(
                    "Skipping collector {CollectorId} in pack {PackId}: no query content",
                    ec.Id, packName);
                _telemetry.RecordQueryBlocked(packName, ec.Id, tenantId, correlationId);
                return;
            }

            // Substitute user-selected date range tokens; falls back to ago(30d)/now() when absent.
            queryContent = SubstituteDateTokens(queryContent, fromUtc, toUtc);

            // Apply App Insights resource filter if discovery identified the linked AI component.
            // Only applied to App Insights table queries; workspace-level tables are unaffected.
            if (appInsightsResourcePath is not null && IsAppInsightsQuery(queryContent))
                queryContent = InjectResourceIdFilter(queryContent, appInsightsResourcePath);

            // Compute API-level timespan to match the KQL date range.
            // Without this the Azure Monitor REST API defaults to PT60M and intersects
            // with the KQL filter, hiding events older than 60 minutes.
            var queryTimespan = (fromUtc.HasValue && toUtc.HasValue)
                ? (TimeSpan?)(toUtc.Value - fromUtc.Value)
                : TimeSpan.FromDays(30); // matches ago(30d) KQL fallback

            // Execute the query — queryContent is passed as parameter, never logged
            var result = await _queryExecutor.ExecuteQueryAsync(workspaceId, queryContent, queryTimespan, ct);

            // Truncate result if needed
            var resultJson   = result.ResultJson;
            var wasTruncated = false;
            if (resultJson is not null && resultJson.Length > maxChars)
            {
                resultJson   = resultJson[..maxChars];
                wasTruncated = true;
            }
            var rowCount = Math.Min(result.RowCount, maxRows);

            evidenceItems.Add(new PackEvidenceItem(
                packName,
                ec.Id,
                connectorName,
                ec.QueryFile,
                queryContent,
                resultJson,
                rowCount,
                result.Success ? null : result.ErrorMessage));

            if (!result.Success)
            {
                _telemetry.RecordQueryFailed(packName, ec.Id, tenantId, result.ErrorMessage ?? "unknown", correlationId);
                errors.Add(
                    $"Pack '{packName}' collector '{ec.Id}': {result.ErrorMessage}");
            }
            else
            {
                _telemetry.RecordCollectorSuccess(packName, ec.Id, tenantId, correlationId);
            }

            if (wasTruncated)
            {
                _telemetry.RecordCollectorTruncated(packName, ec.Id, "max_chars", correlationId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Evidence collector {CollectorId} from pack {PackId} timed out",
                ec.Id, packName);
            _telemetry.RecordQueryTimeout(packName, ec.Id, tenantId, correlationId);
            errors.Add($"Pack '{packName}' collector '{ec.Id}': query timed out");
            evidenceItems.Add(new PackEvidenceItem(
                packName, ec.Id, connectorName,
                ec.QueryFile, null, null, 0, "Query timed out"));
        }
        catch (Exception ex)
        {
            // Log without KQL content — ex.Message from Azure SDK / file I/O, not query text
            _logger.LogWarning(
                ex,
                "Failed to execute evidence collector {CollectorId} from pack {PackId}",
                ec.Id, packName);
            _telemetry.RecordCollectorFailure(packName, ec.Id, tenantId, "exception", correlationId);
            errors.Add($"Pack '{packName}' collector '{ec.Id}': {ex.Message}");
            evidenceItems.Add(new PackEvidenceItem(
                packName, ec.Id, connectorName,
                ec.QueryFile, null, null, 0, ex.Message));
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="mode"/> is at or above "B".
    /// Mode hierarchy: A &lt; B &lt; C (char ordinal comparison).
    /// </summary>
    private static bool IsModeEligible(string mode) =>
        !string.IsNullOrEmpty(mode) && char.ToUpperInvariant(mode[0]) >= 'B';

    /// <summary>
    /// Returns <c>true</c> when <paramref name="packMode"/> is at or below
    /// <paramref name="deploymentMode"/> in the A &lt; B &lt; C hierarchy.
    /// </summary>
    private static string SubstituteDateTokens(string queryContent, DateTime? fromUtc, DateTime? toUtc)
    {
        var from = fromUtc.HasValue
            ? $"datetime({fromUtc.Value.ToUniversalTime():O})"
            : "ago(30d)";
        var to = toUtc.HasValue
            ? $"datetime({toUtc.Value.ToUniversalTime():O})"
            : "now()";
        return queryContent
            .Replace("{FROM_UTC}", from, StringComparison.Ordinal)
            .Replace("{TO_UTC}", to, StringComparison.Ordinal);
    }

    private static bool IsModeAtOrBelow(string packMode, string deploymentMode) =>
        !string.IsNullOrEmpty(packMode) &&
        !string.IsNullOrEmpty(deploymentMode) &&
        char.ToUpperInvariant(packMode[0]) <= char.ToUpperInvariant(deploymentMode[0]);

    private bool IsFeatureEnabled()
    {
        var value = _configuration["Packs:EvidenceExecutionEnabled"];
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    // ── App Insights query detection and _ResourceId filter injection ─────

    private static readonly HashSet<string> AppInsightsTables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AppExceptions", "AppRequests", "AppDependencies", "AppTraces", "AppEvents",
            "AppMetrics", "AppAvailabilityResults", "AppBrowserTimings", "AppPageViews",
            "AppPerformanceCounters", "AppSystemEvents",
        };

    /// <summary>
    /// Returns <c>true</c> when the query's first non-whitespace line starts with an App Insights table name.
    /// </summary>
    private static bool IsAppInsightsQuery(string queryContent)
    {
        var trimmed = queryContent.TrimStart();
        foreach (var table in AppInsightsTables)
        {
            if (trimmed.StartsWith(table + "\n", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(table + "\r", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(table + " ",  StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(table + "|",  StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed.Trim(), table, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Injects a <c>| where _ResourceId has "…"</c> line after the first non-comment,
    /// non-pipe, non-let line (the table name line).
    /// Example: <c>AppExceptions\n| where TimeGenerated &gt; ago(1h)</c>
    /// becomes:  <c>AppExceptions\n| where _ResourceId has "/providers/…"\n| where TimeGenerated &gt; ago(1h)</c>
    /// </summary>
    private static string InjectResourceIdFilter(string queryContent, string resourcePath)
    {
        var lines    = queryContent.Split('\n');
        var injected = false;
        var result   = new List<string>(lines.Length + 1);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            result.Add(line);

            if (!injected && !string.IsNullOrWhiteSpace(line))
            {
                var trimmedLine = line.TrimStart();
                if (!trimmedLine.StartsWith("//") &&
                    !trimmedLine.StartsWith("|") &&
                    !trimmedLine.StartsWith("let ", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add($"| where _ResourceId has \"{resourcePath}\"");
                    injected = true;
                }
            }
        }

        return string.Join('\n', result).TrimEnd();
    }

}
