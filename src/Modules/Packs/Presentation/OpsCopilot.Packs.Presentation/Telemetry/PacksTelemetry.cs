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

    public void Dispose() => _meter.Dispose();
}
