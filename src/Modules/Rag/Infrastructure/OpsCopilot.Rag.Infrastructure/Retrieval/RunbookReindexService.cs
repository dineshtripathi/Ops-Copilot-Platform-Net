using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Application;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

/// <summary>
/// Slice 183 — triggers a full re-index of all runbooks from disk into the
/// active vector store.  Callers (admin endpoint) can use this to pick up
/// newly deployed runbook files without restarting the host.
/// </summary>
internal sealed class RunbookReindexService : IRunbookReindexService
{
    private readonly IRunbookIndexer _indexer;
    private readonly string          _runbookPath;
    private readonly ILogger<RunbookReindexService> _logger;

    public RunbookReindexService(
        IRunbookIndexer                indexer,
        string                         runbookPath,
        ILogger<RunbookReindexService> logger)
    {
        _indexer     = indexer;
        _runbookPath = runbookPath;
        _logger      = logger;
    }

    public async Task<int> ReindexAllAsync(string tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Runbook reindex requested for tenant '{TenantId}' from '{Path}'",
            tenantId, _runbookPath);

        var loaderLogger = _logger as ILogger
                           ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        var docs = RunbookLoader.ToVectorDocuments(_runbookPath, tenantId, loaderLogger);

        if (docs.Count == 0)
        {
            _logger.LogWarning(
                "Runbook reindex: no documents found in '{Path}' for tenant '{TenantId}'",
                _runbookPath, tenantId);
            return 0;
        }

        await _indexer.IndexBatchAsync(docs, ct);

        _logger.LogInformation(
            "Runbook reindex complete: {Count} document(s) submitted for tenant '{TenantId}'",
            docs.Count, tenantId);

        return docs.Count;
    }
}
