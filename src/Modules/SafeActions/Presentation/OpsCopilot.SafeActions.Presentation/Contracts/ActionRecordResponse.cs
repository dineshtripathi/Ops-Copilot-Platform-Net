using OpsCopilot.SafeActions.Domain.Entities;

namespace OpsCopilot.SafeActions.Presentation.Contracts;

/// <summary>
/// Response DTO for an action record.
/// </summary>
public sealed class ActionRecordResponse
{
    public Guid            ActionRecordId         { get; init; }
    public string          TenantId               { get; init; } = string.Empty;
    public Guid            RunId                  { get; init; }
    public string          ActionType             { get; init; } = string.Empty;
    public string          ProposedPayloadJson    { get; init; } = string.Empty;
    public string          Status                 { get; init; } = string.Empty;
    public string          RollbackStatus         { get; init; } = string.Empty;
    public string?         ExecutionPayloadJson   { get; init; }
    public string?         OutcomeJson            { get; init; }
    public string?         RollbackPayloadJson    { get; init; }
    public string?         RollbackOutcomeJson    { get; init; }
    public string?         ManualRollbackGuidance { get; init; }
    public DateTimeOffset  CreatedAtUtc           { get; init; }
    public DateTimeOffset? ExecutedAtUtc          { get; init; }
    public DateTimeOffset? CompletedAtUtc         { get; init; }
    public DateTimeOffset? RolledBackAtUtc        { get; init; }

    public static ActionRecordResponse From(ActionRecord record)
        => new()
        {
            ActionRecordId         = record.ActionRecordId,
            TenantId               = record.TenantId,
            RunId                  = record.RunId,
            ActionType             = record.ActionType,
            ProposedPayloadJson    = record.ProposedPayloadJson,
            Status                 = record.Status.ToString(),
            RollbackStatus         = record.RollbackStatus.ToString(),
            ExecutionPayloadJson   = record.ExecutionPayloadJson,
            OutcomeJson            = record.OutcomeJson,
            RollbackPayloadJson    = record.RollbackPayloadJson,
            RollbackOutcomeJson    = record.RollbackOutcomeJson,
            ManualRollbackGuidance = record.ManualRollbackGuidance,
            CreatedAtUtc           = record.CreatedAtUtc,
            ExecutedAtUtc          = record.ExecutedAtUtc,
            CompletedAtUtc         = record.CompletedAtUtc,
            RolledBackAtUtc        = record.RolledBackAtUtc,
        };
}
