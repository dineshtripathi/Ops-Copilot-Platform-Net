using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Infrastructure.Memory;

/// <summary>
/// Decorator that provides SQL-first, KQL-fallback recall.
/// <list type="bullet">
///   <item>Tries <see cref="SqlIncidentMemoryService"/> first (fast, enriched historical data).</item>
///   <item>If SQL returns no citations (first run, empty DB), falls back to
///         <see cref="LiveKqlIncidentMemoryService"/> which queries Azure Monitor live.</item>
/// </list>
/// </summary>
internal sealed class FallbackIncidentMemoryService : IIncidentMemoryService
{
    private readonly SqlIncidentMemoryService                _sql;
    private readonly LiveKqlIncidentMemoryService            _kql;
    private readonly ILogger<FallbackIncidentMemoryService>  _log;

    public FallbackIncidentMemoryService(
        SqlIncidentMemoryService                sql,
        LiveKqlIncidentMemoryService            kql,
        ILogger<FallbackIncidentMemoryService>  log)
    {
        _sql = sql;
        _kql = kql;
        _log = log;
    }

    public async Task<IReadOnlyList<MemoryCitation>> RecallAsync(
        string            alertFingerprint,
        string            tenantId,
        CancellationToken ct = default)
    {
        var sqlResults = await _sql.RecallAsync(alertFingerprint, tenantId, ct).ConfigureAwait(false);

        if (sqlResults.Count > 0)
        {
            _log.LogDebug(
                "FallbackIncidentMemoryService: returning {Count} SQL citation(s) for tenant {TenantId}",
                sqlResults.Count, tenantId);
            return sqlResults;
        }

        _log.LogDebug(
            "FallbackIncidentMemoryService: SQL returned 0 citations for tenant {TenantId}; falling back to live KQL",
            tenantId);

        return await _kql.RecallAsync(alertFingerprint, tenantId, ct).ConfigureAwait(false);
    }
}
