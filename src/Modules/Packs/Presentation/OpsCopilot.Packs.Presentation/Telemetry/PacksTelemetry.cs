using System.Diagnostics.Metrics;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Presentation.Telemetry;

/// <summary>
/// System.Diagnostics.Metrics implementation of <see cref="IPacksTelemetry"/>.
/// Meter name: <c>OpsCopilot.Packs</c>. All counter names are frozen.
/// </summary>
public sealed class PacksTelemetry : IPacksTelemetry, IDisposable
{
    internal const string MeterName = "OpsCopilot.Packs";

    private readonly Meter _meter;
    private readonly Counter<long> _evidenceAttempts;
    private readonly Counter<long> _evidenceSkipped;
    private readonly Counter<long> _workspaceResolutionFailed;
    private readonly Counter<long> _collectorSuccess;
    private readonly Counter<long> _collectorFailure;
    private readonly Counter<long> _collectorTruncated;
    private readonly Counter<long> _queryBlocked;
    private readonly Counter<long> _queryTimeout;
    private readonly Counter<long> _queryFailed;
    private readonly Counter<long> _safeActionAttempts;
    private readonly Counter<long> _safeActionCreated;
    private readonly Counter<long> _safeActionDenied;
    private readonly Counter<long> _safeActionSkipped;
    private readonly Counter<long> _safeActionFailed;

    public PacksTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _evidenceAttempts          = _meter.CreateCounter<long>("packs.evidence.attempts",                        description: "Evidence execution attempts (Mode B+)");
        _evidenceSkipped           = _meter.CreateCounter<long>("packs.evidence.skipped",                         description: "Evidence execution skipped (Mode A or feature disabled)");
        _workspaceResolutionFailed = _meter.CreateCounter<long>("packs.evidence.workspace_resolution_failed",     description: "Workspace resolution failures");
        _collectorSuccess          = _meter.CreateCounter<long>("packs.evidence.collector.success",               description: "Successful collector executions");
        _collectorFailure          = _meter.CreateCounter<long>("packs.evidence.collector.failure",               description: "Failed collector executions");
        _collectorTruncated        = _meter.CreateCounter<long>("packs.evidence.collector.truncated",             description: "Truncated collector results");
        _queryBlocked              = _meter.CreateCounter<long>("packs.evidence.query.blocked",                   description: "Blocked queries (file not found or no content)");
        _queryTimeout              = _meter.CreateCounter<long>("packs.evidence.query.timeout",                   description: "Query execution timeouts");
        _queryFailed               = _meter.CreateCounter<long>("packs.evidence.query.failed",                    description: "Query execution failures");

        _safeActionAttempts        = _meter.CreateCounter<long>("packs.safeaction.attempts",                      description: "Safe-action recording attempts (Mode C)");
        _safeActionCreated         = _meter.CreateCounter<long>("packs.safeaction.created",                       description: "Safe-action records created successfully");
        _safeActionDenied          = _meter.CreateCounter<long>("packs.safeaction.denied",                        description: "Safe-action records denied by policy");
        _safeActionSkipped         = _meter.CreateCounter<long>("packs.safeaction.skipped",                       description: "Safe-action records skipped (not executable or gate)");
        _safeActionFailed          = _meter.CreateCounter<long>("packs.safeaction.failed",                        description: "Safe-action recording failures");
    }

    public void RecordEvidenceAttempt(string mode, string tenantId, string? correlationId) =>
        _evidenceAttempts.Add(1,
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordEvidenceSkipped(string mode, string tenantId) =>
        _evidenceSkipped.Add(1,
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("tenant_id", tenantId));

    public void RecordWorkspaceResolutionFailed(string tenantId, string errorCode, string? correlationId) =>
        _workspaceResolutionFailed.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("error_code", errorCode),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordCollectorSuccess(string packId, string collectorId, string tenantId, string? correlationId) =>
        _collectorSuccess.Add(1,
            new KeyValuePair<string, object?>("pack_id", packId),
            new KeyValuePair<string, object?>("collector_id", collectorId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordCollectorFailure(string packId, string collectorId, string tenantId, string errorCode, string? correlationId) =>
        _collectorFailure.Add(1,
            new KeyValuePair<string, object?>("pack_id", packId),
            new KeyValuePair<string, object?>("collector_id", collectorId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("error_code", errorCode),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordCollectorTruncated(string packId, string collectorId, string truncateReason, string? correlationId) =>
        _collectorTruncated.Add(1,
            new KeyValuePair<string, object?>("pack_id", packId),
            new KeyValuePair<string, object?>("collector_id", collectorId),
            new KeyValuePair<string, object?>("truncate_reason", truncateReason),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordQueryBlocked(string packId, string collectorId, string tenantId, string? correlationId) =>
        _queryBlocked.Add(1,
            new KeyValuePair<string, object?>("pack_id", packId),
            new KeyValuePair<string, object?>("collector_id", collectorId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordQueryTimeout(string packId, string collectorId, string tenantId, string? correlationId) =>
        _queryTimeout.Add(1,
            new KeyValuePair<string, object?>("pack_id", packId),
            new KeyValuePair<string, object?>("collector_id", collectorId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordQueryFailed(string packId, string collectorId, string tenantId, string errorCode, string? correlationId) =>
        _queryFailed.Add(1,
            new KeyValuePair<string, object?>("pack_id", packId),
            new KeyValuePair<string, object?>("collector_id", collectorId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("error_code", errorCode),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    // ── Safe-action recording counters (Mode C only) ──────────────────────

    public void RecordSafeActionAttempt(string mode, string tenantId, string? correlationId) =>
        _safeActionAttempts.Add(1,
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordSafeActionCreated(string packName, string actionId, string tenantId, string? correlationId) =>
        _safeActionCreated.Add(1,
            new KeyValuePair<string, object?>("pack_name", packName),
            new KeyValuePair<string, object?>("action_id", actionId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordSafeActionDenied(string packName, string actionId, string tenantId, string reasonCode, string? correlationId) =>
        _safeActionDenied.Add(1,
            new KeyValuePair<string, object?>("pack_name", packName),
            new KeyValuePair<string, object?>("action_id", actionId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("reason_code", reasonCode),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void RecordSafeActionSkipped(string packName, string actionId, string tenantId, string? skipReason) =>
        _safeActionSkipped.Add(1,
            new KeyValuePair<string, object?>("pack_name", packName),
            new KeyValuePair<string, object?>("action_id", actionId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("skip_reason", skipReason));

    public void RecordSafeActionFailed(string packName, string actionId, string tenantId, string errorCode, string? correlationId) =>
        _safeActionFailed.Add(1,
            new KeyValuePair<string, object?>("pack_name", packName),
            new KeyValuePair<string, object?>("action_id", actionId),
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("error_code", errorCode),
            new KeyValuePair<string, object?>("correlation_id", correlationId));

    public void Dispose() => _meter.Dispose();
}
