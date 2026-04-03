using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

/// <summary>
/// No-op indexer — registered when <c>Rag:UseVectorRunbooks</c> is false or the
/// vector store / embedding generator dependencies are not configured.
/// </summary>
internal sealed class NullRagRunbookIndexer : IRunbookIndexer
{
    public Task IndexAsync(
        VectorRunbookDocument document,
        CancellationToken     cancellationToken = default)
        => Task.CompletedTask;

    public Task IndexBatchAsync(
        IEnumerable<VectorRunbookDocument> documents,
        CancellationToken                  cancellationToken = default)
        => Task.CompletedTask;
}
