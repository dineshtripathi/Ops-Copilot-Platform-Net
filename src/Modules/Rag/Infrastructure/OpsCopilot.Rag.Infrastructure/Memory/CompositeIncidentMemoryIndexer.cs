using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Memory;

/// <summary>
/// Dual-writes incident memory documents to both a primary and a secondary
/// <see cref="IIncidentMemoryIndexer"/>. Vision §6.7 — composite ingestion sink.
/// A secondary failure is logged and swallowed so the primary write is never lost.
/// </summary>
internal sealed class CompositeIncidentMemoryIndexer : IIncidentMemoryIndexer
{
    private readonly IIncidentMemoryIndexer              _primary;
    private readonly IIncidentMemoryIndexer              _secondary;
    private readonly ILogger<CompositeIncidentMemoryIndexer> _logger;

    public CompositeIncidentMemoryIndexer(
        IIncidentMemoryIndexer                   primary,
        IIncidentMemoryIndexer                   secondary,
        ILogger<CompositeIncidentMemoryIndexer>  logger)
    {
        _primary   = primary;
        _secondary = secondary;
        _logger    = logger;
    }

    public async Task IndexAsync(
        IncidentMemoryDocument document,
        CancellationToken      cancellationToken = default)
    {
        await _primary.IndexAsync(document, cancellationToken);

        try
        {
            await _secondary.IndexAsync(document, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Secondary incident memory indexer failed for run '{RunId}'. Primary write succeeded.",
                document.RunId);
        }
    }
}
