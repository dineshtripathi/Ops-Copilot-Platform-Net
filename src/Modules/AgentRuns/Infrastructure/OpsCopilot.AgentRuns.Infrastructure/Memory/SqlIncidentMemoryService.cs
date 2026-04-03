using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;

namespace OpsCopilot.AgentRuns.Infrastructure.Memory;

/// <summary>
/// SQL-backed implementation of <see cref="IIncidentMemoryService"/>.
/// Returns the tenant's most-frequent failure patterns from the last 14 days
/// without requiring a vector store.  Used as the default recall implementation;
/// overridden by <see cref="RagBackedIncidentMemoryService"/> when
/// <c>AgentRuns:IncidentRecall:Enabled=true</c>.
/// </summary>
internal sealed class SqlIncidentMemoryService : IIncidentMemoryService
{
    private const int  WindowDays  = 14;
    private const int  MaxPatterns = 10;

    private readonly AgentRunsDbContext              _db;
    private readonly ILogger<SqlIncidentMemoryService> _log;

    public SqlIncidentMemoryService(
        AgentRunsDbContext              db,
        ILogger<SqlIncidentMemoryService> log)
    {
        _db  = db;
        _log = log;
    }

    public async Task<IReadOnlyList<MemoryCitation>> RecallAsync(
        string alertFingerprint, string tenantId, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-WindowDays);

        // Project the scalar fields we need before grouping so EF can translate to SQL.
        // Includes any Azure resource incident:
        //   • Failed / Degraded runs of every alert type (metric, log, activity, custom)
        //   • Completed runs that carried an exception signal — these resolved but still
        //     represent a real past exception across any resource and should inform recall.
        var rows = await _db.AgentRuns
            .Where(r => r.TenantId         == tenantId
                     && r.CreatedAtUtc     >= since
                     && r.AlertFingerprint != null
                     && (r.Status == AgentRunStatus.Failed
                      || r.Status == AgentRunStatus.Degraded
                      || (r.Status == AgentRunStatus.Completed && r.IsExceptionSignal)))
            .Select(r => new
            {
                r.RunId,
                r.AlertFingerprint,
                r.AlertSourceType,
                r.AlertProvider,
                r.IsExceptionSignal,
                r.AzureApplication,
                r.AzureResourceGroup,
                r.AzureResourceId,
                r.CreatedAtUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Group in memory — avoids EF translation complexity for grouped aggregates.
        var patterns = rows
            .GroupBy(r => r.AlertFingerprint!)
            .Select(g =>
            {
                var latest = g.OrderByDescending(r => r.CreatedAtUtc).First();
                return new
                {
                    AlertFingerprint = g.Key,
                    Count            = g.Count(),
                    Latest           = latest,
                    // Take the most descriptive available value from any run in the group.
                    IsException      = g.Any(r => r.IsExceptionSignal),
                    AlertSourceType  = g.Select(r => r.AlertSourceType).FirstOrDefault(t => t != null),
                    AlertProvider    = g.Select(r => r.AlertProvider).FirstOrDefault(p => p != null),
                    AzureApplication = g.Select(r => r.AzureApplication).FirstOrDefault(a => a != null),
                    AzureResourceGroup = g.Select(r => r.AzureResourceGroup).FirstOrDefault(rg => rg != null),
                    AzureResourceId  = g.Select(r => r.AzureResourceId).FirstOrDefault(id => id != null),
                };
            })
            .OrderByDescending(p => p.Count)
            .Take(MaxPatterns)
            .ToList();

        _log.LogDebug(
            "SqlIncidentMemoryService recalled {Count} failure pattern(s) for tenant {TenantId} over {Days} days.",
            patterns.Count, tenantId, WindowDays);

        return patterns
            .Select(p => new MemoryCitation(
                RunId:            p.Latest.RunId.ToString(),
                AlertFingerprint: p.AlertFingerprint,
                SummarySnippet:   BuildSnippet(p.Count, WindowDays, p.IsException,
                                               p.AlertSourceType, p.AlertProvider,
                                               p.AzureApplication, p.AzureResourceGroup,
                                               p.AzureResourceId, p.Latest.CreatedAtUtc),
                Score:            0.0,
                CreatedAtUtc:     p.Latest.CreatedAtUtc))
            .ToList();
    }

    /// <summary>
    /// Builds a human-readable snippet that grounds the LLM in real tenant data.
    /// Covers any Azure resource incident type — metric, log, activity-log, App Exception,
    /// or any custom alert — using whichever classification fields were stored on the run.
    /// Falls back gracefully when fields are absent so existing callers are unaffected.
    /// </summary>
    private static string BuildSnippet(
        int            count,
        int            windowDays,
        bool           isException,
        string?        alertSourceType,
        string?        alertProvider,
        string?        azureApplication,
        string?        azureResourceGroup,
        string?        azureResourceId,
        DateTimeOffset latestAt)
    {
        var typeParts = new List<string>(2);
        if (isException)
            typeParts.Add("App Exception");
        else if (alertSourceType is { Length: > 0 })
            typeParts.Add(alertSourceType);

        if (alertProvider is { Length: > 0 })
            typeParts.Add($"[{alertProvider}]");

        var typePrefix = typeParts.Count > 0 ? string.Join(" ", typeParts) + " " : string.Empty;

        // Resource context — prefer application name, fall back to resource group, then resource ID leaf.
        var contextParts = new List<string>(3);
        if (azureApplication is { Length: > 0 })
            contextParts.Add($"app '{azureApplication}'");
        if (azureResourceGroup is { Length: > 0 })
            contextParts.Add($"rg '{azureResourceGroup}'");
        if (azureResourceId is { Length: > 0 })
        {
            // Use only the leaf segment (after the last '/') to keep snippets concise.
            var leaf = azureResourceId.TrimEnd('/');
            var slash = leaf.LastIndexOf('/');
            var resourceName = slash >= 0 ? leaf[(slash + 1)..] : leaf;
            if (resourceName.Length > 0 && resourceName != azureApplication)
                contextParts.Add($"resource '{resourceName}'");
        }

        var contextSuffix = contextParts.Count > 0 ? ", " + string.Join(", ", contextParts) : string.Empty;

        return $"{count} {typePrefix}failure(s){contextSuffix} in last {windowDays} days, most recent {latestAt:yyyy-MM-dd HH:mm} UTC";
    }
}
