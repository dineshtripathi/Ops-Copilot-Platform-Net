namespace OpsCopilot.Rag.Application;

/// <summary>
/// A single hit returned from a runbook search.
/// </summary>
/// <param name="RunbookId">Unique identifier of the runbook document.</param>
/// <param name="Title">Title of the matching runbook.</param>
/// <param name="Snippet">Relevant content snippet from the runbook.</param>
/// <param name="Score">Relevance score (0.0–1.0, higher is better).</param>
public sealed record RunbookSearchResult(
    string RunbookId,
    string Title,
    string Snippet,
    double Score);
