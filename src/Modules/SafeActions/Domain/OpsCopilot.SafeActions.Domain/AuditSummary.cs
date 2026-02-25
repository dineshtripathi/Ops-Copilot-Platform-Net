namespace OpsCopilot.SafeActions.Domain;

/// <summary>
/// Read-only projection summarising execution logs and approval records
/// for a single action record. Used to enrich list-endpoint responses.
/// </summary>
public sealed record AuditSummary(
    int             ExecutionLogCount,
    DateTimeOffset? LastExecutionAtUtc,
    bool?           LastExecutionSuccess,
    int             ApprovalCount,
    string?         LastApprovalDecision,
    DateTimeOffset? LastApprovalAtUtc)
{
    /// <summary>Default summary when no audit data exists.</summary>
    public static readonly AuditSummary Empty = new(0, null, null, 0, null, null);
}
