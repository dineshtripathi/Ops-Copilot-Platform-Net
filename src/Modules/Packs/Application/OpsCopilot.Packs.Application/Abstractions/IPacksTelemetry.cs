namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Emits System.Diagnostics.Metrics counters for pack evidence execution observability.
/// All counter names are frozen — do not rename without a migration plan.
/// </summary>
public interface IPacksTelemetry
{
    /// <summary>packs.evidence.attempts — incremented when evidence execution begins (Mode B+).</summary>
    void RecordEvidenceAttempt(string mode, string tenantId, string? correlationId);

    /// <summary>packs.evidence.skipped — incremented when evidence execution is skipped (Mode A or feature disabled).</summary>
    void RecordEvidenceSkipped(string mode, string tenantId);

    /// <summary>packs.evidence.workspace_resolution_failed — incremented when workspace resolution fails.</summary>
    void RecordWorkspaceResolutionFailed(string tenantId, string errorCode, string? correlationId);

    /// <summary>packs.evidence.collector.success — incremented when a collector executes successfully.</summary>
    void RecordCollectorSuccess(string packId, string collectorId, string tenantId, string? correlationId);

    /// <summary>packs.evidence.collector.failure — incremented when a collector fails with an exception.</summary>
    void RecordCollectorFailure(string packId, string collectorId, string tenantId, string errorCode, string? correlationId);

    /// <summary>packs.evidence.collector.truncated — incremented when a collector result is truncated.</summary>
    void RecordCollectorTruncated(string packId, string collectorId, string truncateReason, string? correlationId);

    /// <summary>packs.evidence.query.blocked — incremented when a query cannot execute (file not found, no content).</summary>
    void RecordQueryBlocked(string packId, string collectorId, string tenantId, string? correlationId);

    /// <summary>packs.evidence.query.timeout — incremented when a query execution times out.</summary>
    void RecordQueryTimeout(string packId, string collectorId, string tenantId, string? correlationId);

    /// <summary>packs.evidence.query.failed — incremented when a query execution returns a failure result.</summary>
    void RecordQueryFailed(string packId, string collectorId, string tenantId, string errorCode, string? correlationId);

    // ── Safe-action recording counters (Mode C only) ──────────────────────

    /// <summary>packs.safeaction.attempts — incremented when safe-action recording begins (Mode C).</summary>
    void RecordSafeActionAttempt(string mode, string tenantId, string? correlationId);

    /// <summary>packs.safeaction.created — incremented when a safe-action record is created successfully.</summary>
    void RecordSafeActionCreated(string packName, string actionId, string tenantId, string? correlationId);

    /// <summary>packs.safeaction.denied — incremented when a safe-action is denied by policy.</summary>
    void RecordSafeActionDenied(string packName, string actionId, string tenantId, string reasonCode, string? correlationId);

    /// <summary>packs.safeaction.skipped — incremented when a safe-action is skipped (not executable or gate).</summary>
    void RecordSafeActionSkipped(string packName, string actionId, string tenantId, string? skipReason);

    /// <summary>packs.safeaction.failed — incremented when a safe-action recording fails with an exception.</summary>
    void RecordSafeActionFailed(string packName, string actionId, string tenantId, string errorCode, string? correlationId);
}
