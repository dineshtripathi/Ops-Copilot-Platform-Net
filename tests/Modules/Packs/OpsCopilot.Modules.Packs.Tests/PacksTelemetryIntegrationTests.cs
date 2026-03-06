using System.Diagnostics.Metrics;
using OpsCopilot.Packs.Presentation.Telemetry;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Integration tests for <see cref="PacksTelemetry"/> verifying that
/// real <see cref="System.Diagnostics.Metrics"/> counters emit with
/// correct instrument names, values, and tag key-value pairs.
/// </summary>
public sealed class PacksTelemetryIntegrationTests : IDisposable
{
    private const string ExpectedMeterName = "OpsCopilot.Packs";

    private readonly List<CapturedMeasurement> _measurements = [];
    private readonly MeterListener _listener;
    private readonly PacksTelemetry _telemetry;

    public PacksTelemetryIntegrationTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ExpectedMeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) =>
            {
                _measurements.Add(new CapturedMeasurement(
                    instrument.Name, measurement, tags.ToArray()));
            });
        _listener.Start();
        _telemetry = new PacksTelemetry();
    }

    public void Dispose()
    {
        _telemetry.Dispose();
        _listener.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. packs.evidence.attempts
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordEvidenceAttempt_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordEvidenceAttempt("B", "tenant-1", "corr-001");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.attempts", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("B", Tag(m, "mode"));
        Assert.Equal("tenant-1", Tag(m, "tenant_id"));
        Assert.Equal("corr-001", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. packs.evidence.skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordEvidenceSkipped_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordEvidenceSkipped("A", "tenant-2");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.skipped", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("A", Tag(m, "mode"));
        Assert.Equal("tenant-2", Tag(m, "tenant_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. packs.evidence.workspace_resolution_failed
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordWorkspaceResolutionFailed_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordWorkspaceResolutionFailed("tenant-3", "missing_workspace", "corr-ws");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.workspace_resolution_failed", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("tenant-3", Tag(m, "tenant_id"));
        Assert.Equal("missing_workspace", Tag(m, "error_code"));
        Assert.Equal("corr-ws", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. packs.evidence.collector.success
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordCollectorSuccess_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordCollectorSuccess("azure-vm", "ec1", "tenant-4", "corr-cs");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.collector.success", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("azure-vm", Tag(m, "pack_id"));
        Assert.Equal("ec1", Tag(m, "collector_id"));
        Assert.Equal("tenant-4", Tag(m, "tenant_id"));
        Assert.Equal("corr-cs", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. packs.evidence.collector.failure
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordCollectorFailure_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordCollectorFailure("azure-vm", "ec2", "tenant-5", "exception", "corr-cf");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.collector.failure", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("azure-vm", Tag(m, "pack_id"));
        Assert.Equal("ec2", Tag(m, "collector_id"));
        Assert.Equal("tenant-5", Tag(m, "tenant_id"));
        Assert.Equal("exception", Tag(m, "error_code"));
        Assert.Equal("corr-cf", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. packs.evidence.collector.truncated
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordCollectorTruncated_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordCollectorTruncated("azure-vm", "ec3", "max_chars", "corr-tr");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.collector.truncated", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("azure-vm", Tag(m, "pack_id"));
        Assert.Equal("ec3", Tag(m, "collector_id"));
        Assert.Equal("max_chars", Tag(m, "truncate_reason"));
        Assert.Equal("corr-tr", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. packs.evidence.query.blocked
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordQueryBlocked_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordQueryBlocked("azure-vm", "ec4", "tenant-7", "corr-qb");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.query.blocked", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("azure-vm", Tag(m, "pack_id"));
        Assert.Equal("ec4", Tag(m, "collector_id"));
        Assert.Equal("tenant-7", Tag(m, "tenant_id"));
        Assert.Equal("corr-qb", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. packs.evidence.query.timeout
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordQueryTimeout_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordQueryTimeout("azure-vm", "ec5", "tenant-8", "corr-to");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.query.timeout", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("azure-vm", Tag(m, "pack_id"));
        Assert.Equal("ec5", Tag(m, "collector_id"));
        Assert.Equal("tenant-8", Tag(m, "tenant_id"));
        Assert.Equal("corr-to", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. packs.evidence.query.failed
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordQueryFailed_EmitsCorrectCounterAndTags()
    {
        _telemetry.RecordQueryFailed("azure-vm", "ec6", "tenant-9", "Syntax error", "corr-qf");

        var m = Assert.Single(_measurements);
        Assert.Equal("packs.evidence.query.failed", m.InstrumentName);
        Assert.Equal(1L, m.Value);
        Assert.Equal("azure-vm", Tag(m, "pack_id"));
        Assert.Equal("ec6", Tag(m, "collector_id"));
        Assert.Equal("tenant-9", Tag(m, "tenant_id"));
        Assert.Equal("Syntax error", Tag(m, "error_code"));
        Assert.Equal("corr-qf", Tag(m, "correlation_id"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private sealed record CapturedMeasurement(
        string InstrumentName,
        long Value,
        KeyValuePair<string, object?>[] Tags);

    private static object? Tag(CapturedMeasurement m, string name) =>
        m.Tags.FirstOrDefault(t => t.Key == name).Value;
}
