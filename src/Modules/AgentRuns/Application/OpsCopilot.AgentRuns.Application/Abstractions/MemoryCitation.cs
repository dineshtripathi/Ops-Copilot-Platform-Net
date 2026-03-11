namespace OpsCopilot.AgentRuns.Application.Abstractions;

public sealed record MemoryCitation(
    string         RunId,
    string         AlertFingerprint,
    string         SummarySnippet,
    double         Score,
    DateTimeOffset CreatedAtUtc);
