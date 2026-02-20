namespace OpsCopilot.AgentRuns.Domain.Entities;

/// <summary>
/// INSERT-only ledger row. Represents a single MCP tool call made during a run.
/// Once created, no fields are updated (the row is immutable).
/// </summary>
public sealed class ToolCall
{
    // EF Core constructor
    private ToolCall() { }

    public static ToolCall Create(
        Guid    runId,
        string  toolName,
        string  requestJson,
        string? responseJson,
        string  status,
        long    durationMs,
        string? citationsJson)
        => new()
        {
            ToolCallId    = Guid.NewGuid(),
            RunId         = runId,
            ToolName      = toolName,
            RequestJson   = requestJson,
            ResponseJson  = responseJson,
            Status        = status,
            DurationMs    = durationMs,
            CitationsJson = citationsJson,
            ExecutedAtUtc = DateTimeOffset.UtcNow,
        };

    public Guid            ToolCallId    { get; private set; }
    public Guid            RunId         { get; private set; }
    public string          ToolName      { get; private set; } = string.Empty;
    public string          RequestJson   { get; private set; } = string.Empty;
    public string?         ResponseJson  { get; private set; }
    public string          Status        { get; private set; } = string.Empty;  // "Success" | "Failed"
    public long            DurationMs    { get; private set; }
    public string?         CitationsJson { get; private set; }
    public DateTimeOffset  ExecutedAtUtc { get; private set; }
}
