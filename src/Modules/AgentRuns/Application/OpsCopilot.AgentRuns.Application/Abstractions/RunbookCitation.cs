namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Evidence record for runbook search results attached to triage responses.
/// All fields come directly from the tool execution — no inference or enrichment.
/// </summary>
public sealed record RunbookCitation(
    string RunbookId,
    string Title,
    string Snippet,
    double Score);
