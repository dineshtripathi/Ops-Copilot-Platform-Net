using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Memory;

/// <summary>
/// No-op indexer — registered when <c>Rag:UseVectorMemory</c> is false or the
/// vector store / embedding generator dependencies are not configured.
/// </summary>
internal sealed class NullRagIncidentMemoryIndexer : IIncidentMemoryIndexer
{
    public Task IndexAsync(
        IncidentMemoryDocument document,
        CancellationToken      cancellationToken = default)
        => Task.CompletedTask;
}
