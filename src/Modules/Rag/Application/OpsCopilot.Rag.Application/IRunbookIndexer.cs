using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Application;

/// <summary>
/// Indexes runbook documents into the vector store so they can be retrieved
/// by <see cref="IRunbookRetrievalService"/> during triage.
/// </summary>
public interface IRunbookIndexer
{
    /// <summary>
    /// Embeds and upserts a single <see cref="VectorRunbookDocument"/> into the store.
    /// The caller is responsible for populating all non-embedding fields before calling.
    /// </summary>
    Task IndexAsync(
        VectorRunbookDocument document,
        CancellationToken     cancellationToken = default);

    /// <summary>
    /// Embeds and upserts a batch of documents. Implementations may parallelise
    /// the embedding calls; callers should treat order as undefined.
    /// </summary>
    Task IndexBatchAsync(
        IEnumerable<VectorRunbookDocument> documents,
        CancellationToken                  cancellationToken = default);
}
