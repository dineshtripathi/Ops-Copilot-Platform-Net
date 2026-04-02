namespace OpsCopilot.Rag.Application;

/// <summary>
/// Slice 183 — admin service that re-ingests all runbooks from disk into
/// the active vector store so that newly added or edited runbooks become
/// immediately searchable without restarting the host.
/// </summary>
public interface IRunbookReindexService
{
    /// <summary>
    /// Loads every markdown file from the configured runbook directory,
    /// converts them to <see cref="Domain.VectorRunbookDocument"/> instances,
    /// and calls <see cref="IRunbookIndexer.IndexBatchAsync"/> to upsert them.
    /// </summary>
    /// <param name="tenantId">
    /// Tenant whose runbooks are being re-indexed.
    /// Used to derive the deterministic document <see cref="System.Guid"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Count of runbook documents submitted to the indexer.</returns>
    Task<int> ReindexAllAsync(string tenantId, CancellationToken ct = default);
}
