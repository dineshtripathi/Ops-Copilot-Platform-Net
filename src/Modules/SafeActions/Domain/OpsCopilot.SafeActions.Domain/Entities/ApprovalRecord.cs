using OpsCopilot.SafeActions.Domain.Enums;

namespace OpsCopilot.SafeActions.Domain.Entities;

/// <summary>
/// INSERT-only audit record. Captures an approval or rejection decision
/// for an action execution or rollback request.
/// Once created, no fields are updated (the row is immutable).
/// </summary>
public sealed class ApprovalRecord
{
    // EF Core constructor
    private ApprovalRecord() { }

    public static ApprovalRecord Create(
        Guid   actionRecordId,
        string approverIdentity,
        ApprovalDecision decision,
        string reason,
        string target)
        => new()
        {
            ApprovalId       = Guid.NewGuid(),
            ActionRecordId   = actionRecordId,
            ApproverIdentity = approverIdentity,
            Decision         = decision,
            Reason           = reason,
            Target           = target,
            CreatedAtUtc     = DateTimeOffset.UtcNow,
        };

    public Guid             ApprovalId       { get; private set; }
    public Guid             ActionRecordId   { get; private set; }
    public string           ApproverIdentity { get; private set; } = string.Empty;
    public ApprovalDecision Decision         { get; private set; }
    public string           Reason           { get; private set; } = string.Empty;
    public string           Target           { get; private set; } = string.Empty;  // "Action" | "Rollback"
    public DateTimeOffset   CreatedAtUtc     { get; private set; }
}
