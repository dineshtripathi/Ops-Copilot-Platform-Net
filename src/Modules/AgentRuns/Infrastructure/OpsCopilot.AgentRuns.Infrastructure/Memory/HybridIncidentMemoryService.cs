using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Infrastructure.Memory;

/// <summary>
/// AI-primary incident memory: SQL exact-match → RAG semantic search → live KQL.
///
/// Chain:
///   1. SqlIncidentMemoryService  — fast, enriched historical data from relational store.
///   2. RagBackedIncidentMemoryService — semantic vector recall via configured backend.
///   3. LiveKqlIncidentMemoryService  — live Azure Monitor query as last resort.
///
/// Auto-activates when <c>Rag:VectorBackend</c> is not "InMemory" (i.e. AzureAISearch or Qdrant).
/// This upholds §6.1 Hard Invariant: vector recall is the default path in every production
/// deployment, not guarded behind opt-in config flags. Slice 178.
/// </summary>
internal sealed class HybridIncidentMemoryService : IIncidentMemoryService
{
    private readonly SqlIncidentMemoryService             _sql;
    private readonly RagBackedIncidentMemoryService       _rag;
    private readonly LiveKqlIncidentMemoryService         _kql;
    private readonly ILogger<HybridIncidentMemoryService> _log;

    public HybridIncidentMemoryService(
        SqlIncidentMemoryService             sql,
        RagBackedIncidentMemoryService       rag,
        LiveKqlIncidentMemoryService         kql,
        ILogger<HybridIncidentMemoryService> log)
    {
        _sql = sql;
        _rag = rag;
        _kql = kql;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryCitation>> RecallAsync(
        string            alertFingerprint,
        string            tenantId,
        CancellationToken cancellationToken = default)
    {
        // 1. SQL — exact symbolic match, zero latency overhead.
        var sqlResults = await _sql.RecallAsync(alertFingerprint, tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (sqlResults.Count > 0)
        {
            _log.LogDebug(
                "HybridIncidentMemoryService: SQL hit — returning {Count} citation(s) for tenant {TenantId}",
                sqlResults.Count, tenantId);
            return sqlResults;
        }

        // 2. RAG — semantic vector recall (AI-primary path).
        var ragResults = await _rag.RecallAsync(alertFingerprint, tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (ragResults.Count > 0)
        {
            _log.LogDebug(
                "HybridIncidentMemoryService: RAG hit — returning {Count} citation(s) for tenant {TenantId}",
                ragResults.Count, tenantId);
            return ragResults;
        }

        // 3. KQL — live Azure Monitor query as last resort.
        _log.LogDebug(
            "HybridIncidentMemoryService: SQL + RAG returned 0 citations for tenant {TenantId}; " +
            "falling back to live KQL query",
            tenantId);

        return await _kql.RecallAsync(alertFingerprint, tenantId, cancellationToken)
            .ConfigureAwait(false);
    }
}
