namespace OpsCopilot.Rag.Application;

/// <summary>
/// Application-facing abstraction for runbook retrieval.
/// Infrastructure provides the concrete implementation (in-memory for dev,
/// AI Search or similar for production).
/// </summary>
public interface IRunbookRetrievalService
{
    /// <summary>
    /// Searches for runbook documents matching the given query.
    /// </summary>
    Task<IReadOnlyList<RunbookSearchResult>> SearchAsync(
        RunbookSearchQuery query,
        CancellationToken ct = default);
}
