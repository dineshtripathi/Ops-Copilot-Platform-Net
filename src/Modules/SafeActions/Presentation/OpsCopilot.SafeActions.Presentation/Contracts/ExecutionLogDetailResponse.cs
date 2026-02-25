using OpsCopilot.SafeActions.Domain.Entities;

namespace OpsCopilot.SafeActions.Presentation.Contracts;

/// <summary>
/// Detail DTO for an individual execution log entry in a GET-by-id response.
/// Payload fields are redacted to strip sensitive keys before serialisation.
/// </summary>
public sealed class ExecutionLogDetailResponse
{
    public Guid            ExecutionLogId      { get; init; }
    public string          ExecutionType       { get; init; } = string.Empty;
    public bool            Success             { get; init; }
    public long            DurationMs          { get; init; }
    public DateTimeOffset  ExecutedAtUtc       { get; init; }
    public string          RequestPayloadJson  { get; init; } = string.Empty;
    public string?         ResponsePayloadJson { get; init; }

    public static ExecutionLogDetailResponse From(ExecutionLog log)
        => new()
        {
            ExecutionLogId      = log.ExecutionLogId,
            ExecutionType       = log.ExecutionType,
            Success             = log.Status == "Success",
            DurationMs          = log.DurationMs,
            ExecutedAtUtc       = log.ExecutedAtUtc,
            RequestPayloadJson  = PayloadRedactor.Redact(log.RequestPayloadJson)!,
            ResponsePayloadJson = PayloadRedactor.Redact(log.ResponsePayloadJson),
        };
}
