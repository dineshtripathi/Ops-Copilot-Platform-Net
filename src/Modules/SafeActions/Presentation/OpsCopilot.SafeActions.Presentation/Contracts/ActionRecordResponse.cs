using OpsCopilot.SafeActions.Domain;
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

    // ── Catalog-derived risk tier (null when catalog is empty) ──
    public string?         RiskTier               { get; init; }

    // ── Audit-summary enrichment fields ─────────────────────────
    public int             ExecutionLogCount      { get; init; }
    public DateTimeOffset? LastExecutionAtUtc      { get; init; }
    public bool?           LastExecutionSuccess    { get; init; }
    public int             ApprovalCount           { get; init; }
    public string?         LastApprovalDecision    { get; init; }
    public DateTimeOffset? LastApprovalAtUtc       { get; init; }

    // ── Detail collections (populated only on GET-by-id) ────────
    public IReadOnlyList<ApprovalDetailResponse>      Approvals     { get; init; } = Array.Empty<ApprovalDetailResponse>();
    public IReadOnlyList<ExecutionLogDetailResponse>   ExecutionLogs { get; init; } = Array.Empty<ExecutionLogDetailResponse>();

    public static ActionRecordResponse From(ActionRecord record, string? riskTier = null)
        => From(record, AuditSummary.Empty, riskTier);

    public static ActionRecordResponse From(ActionRecord record, AuditSummary audit, string? riskTier = null)
        => From(record, audit, Array.Empty<ApprovalRecord>(), Array.Empty<ExecutionLog>(), riskTier);

    public static ActionRecordResponse From(
        ActionRecord                  record,
        AuditSummary                  audit,
        IReadOnlyList<ApprovalRecord> approvals,
        IReadOnlyList<ExecutionLog>   executionLogs,
        string?                       riskTier = null)
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
            RiskTier               = riskTier,
            ExecutionLogCount      = audit.ExecutionLogCount,
            LastExecutionAtUtc     = audit.LastExecutionAtUtc,
            LastExecutionSuccess   = audit.LastExecutionSuccess,
            ApprovalCount          = audit.ApprovalCount,
            LastApprovalDecision   = audit.LastApprovalDecision,
            LastApprovalAtUtc      = audit.LastApprovalAtUtc,
            Approvals              = approvals.Select(ApprovalDetailResponse.From).ToList(),
            ExecutionLogs          = executionLogs.Select(ExecutionLogDetailResponse.From).ToList(),
        };
}
