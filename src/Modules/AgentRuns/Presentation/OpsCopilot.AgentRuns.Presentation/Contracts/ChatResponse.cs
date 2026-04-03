namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>A single incident memory hit returned from the chat endpoint.</summary>
public sealed record MemoryCitationDto(
    string          RunId,
    string          AlertFingerprint,
    string          SummarySnippet,
    double          Score,
    DateTimeOffset  CreatedAtUtc);

/// <summary>Response body for POST /agent/chat.</summary>
public sealed record ChatResponse(
    string                          Answer,
    IReadOnlyList<MemoryCitationDto>    MemoryCitations,
    IReadOnlyList<RunbookCitationDto>   RunbookCitations);
