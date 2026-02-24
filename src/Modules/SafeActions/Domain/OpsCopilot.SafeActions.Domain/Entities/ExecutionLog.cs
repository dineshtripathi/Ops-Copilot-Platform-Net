namespace OpsCopilot.SafeActions.Domain.Entities;

/// <summary>
/// INSERT-only ledger row. Records an execution or rollback attempt.
/// Once created, no fields are updated (the row is immutable).
/// </summary>
public sealed class ExecutionLog
{
    // EF Core constructor
    private ExecutionLog() { }

    public static ExecutionLog Create(
        Guid    actionRecordId,
        string  executionType,        // "Execute" | "Rollback"
        string  requestPayloadJson,
        string? responsePayloadJson,
        string  status,               // "Success" | "Failed"
        long    durationMs)
        => new()
        {
            ExecutionLogId      = Guid.NewGuid(),
            ActionRecordId      = actionRecordId,
            ExecutionType       = executionType,
            RequestPayloadJson  = requestPayloadJson,
            ResponsePayloadJson = responsePayloadJson,
            Status              = status,
            DurationMs          = durationMs,
            ExecutedAtUtc       = DateTimeOffset.UtcNow,
        };

    public Guid           ExecutionLogId      { get; private set; }
    public Guid           ActionRecordId      { get; private set; }
    public string         ExecutionType       { get; private set; } = string.Empty;
    public string         RequestPayloadJson  { get; private set; } = string.Empty;
    public string?        ResponsePayloadJson { get; private set; }
    public string         Status              { get; private set; } = string.Empty;
    public long           DurationMs          { get; private set; }
    public DateTimeOffset ExecutedAtUtc       { get; private set; }
}
