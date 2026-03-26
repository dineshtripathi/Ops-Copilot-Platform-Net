using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Result returned by <see cref="ChatOrchestrator.ChatAsync"/>.
/// </summary>
public sealed record ChatResult(
    string                         Answer,
    IReadOnlyList<MemoryCitation>  MemoryCitations,
    IReadOnlyList<RunbookCitation> RunbookCitations);
