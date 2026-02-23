namespace OpsCopilot.Rag.Domain;

/// <summary>
/// Represents a runbook document stored in the RAG knowledge base.
/// Each document has a unique identifier, title, content, and optional tags
/// used for keyword matching during retrieval.
/// </summary>
public sealed class RunbookDocument
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
