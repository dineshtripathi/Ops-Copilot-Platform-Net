using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

/// <summary>
/// Dual-writes indexed runbook documents to both a primary and a secondary
/// <see cref="IRunbookIndexer"/>. Vision §6.7 — composite ingestion sink.
/// The primary write is mandatory; a secondary failure is logged and swallowed
/// so the primary result is never lost.
/// </summary>
internal sealed class CompositeRunbookIndexer : IRunbookIndexer
{
    private readonly IRunbookIndexer              _primary;
    private readonly IRunbookIndexer              _secondary;
    private readonly ILogger<CompositeRunbookIndexer> _logger;

    public CompositeRunbookIndexer(
        IRunbookIndexer                   primary,
        IRunbookIndexer                   secondary,
        ILogger<CompositeRunbookIndexer>  logger)
    {
        _primary   = primary;
        _secondary = secondary;
        _logger    = logger;
    }

    public async Task IndexAsync(
        VectorRunbookDocument document,
        CancellationToken     cancellationToken = default)
    {
        await _primary.IndexAsync(document, cancellationToken);

        try
        {
            await _secondary.IndexAsync(document, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Secondary runbook indexer failed for document '{RunbookId}'. Primary write succeeded.",
                document.RunbookId);
        }
    }

    public async Task IndexBatchAsync(
        IEnumerable<VectorRunbookDocument> documents,
        CancellationToken                  cancellationToken = default)
    {
        foreach (var doc in documents)
            await IndexAsync(doc, cancellationToken);
    }
}
