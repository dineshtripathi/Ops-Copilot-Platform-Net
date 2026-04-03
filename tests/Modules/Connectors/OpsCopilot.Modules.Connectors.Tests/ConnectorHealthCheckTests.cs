using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Connectors.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Connectors.Tests;

/// <summary>
/// Tests for Slice 163: Connector Credential Health + Rotation domain types.
/// </summary>
public sealed class ConnectorHealthCheckTests
{
    // ── Helpers ─────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ConnectorHealthCheckRunner MakeRunner(
        IConfiguration config,
        ILogger<ConnectorHealthCheckRunner>? logger = null)
    {
        var credProvider = new KeyVaultConnectorCredentialProvider(config);
        var log          = logger ?? new LoggerFactory().CreateLogger<ConnectorHealthCheckRunner>();
        return new ConnectorHealthCheckRunner(credProvider, log);
    }

    // ── Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_CredentialFound_ReturnsHealthyReport()
    {
        var config = BuildConfig(new() { ["connector-tenant1-azuremonitor"] = "secret-value" });
        var sut    = MakeRunner(config);

        var report = await sut.CheckAsync("tenant1", "azuremonitor");

        Assert.True(report.IsHealthy);
        Assert.Equal("azuremonitor", report.ConnectorName);
        Assert.Null(report.FailureReason);
        Assert.True(report.CheckedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CheckAsync_NoCredential_ReturnsUnhealthyReport()
    {
        var config = BuildConfig(new());    // empty — no key configured
        var sut    = MakeRunner(config);

        var report = await sut.CheckAsync("tenant1", "azuremonitor");

        Assert.False(report.IsHealthy);
        Assert.Equal("azuremonitor", report.ConnectorName);
        Assert.NotNull(report.FailureReason);
        Assert.Contains("azuremonitor", report.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_NoCredential_LogsWarning()
    {
        var config  = BuildConfig(new());
        var records = new List<(LogLevel Level, string Message)>();
        var logger  = new CapturingLogger<ConnectorHealthCheckRunner>(records);
        var sut     = MakeRunner(config, logger);

        await sut.CheckAsync("tenant1", "azuremonitor");

        Assert.Contains(records, r => r.Level == LogLevel.Warning);
    }

    // ── Domain-type smoke tests ──────────────────────────────────

    [Fact]
    public void CredentialRotationMetadata_DefaultStatus_IsUnknown()
    {
        var meta = new CredentialRotationMetadata(
            ConnectorName:  "azuremonitor",
            LastRotatedAt:  null,
            ExpiresAt:      null,
            Status:         RotationStatus.Unknown);

        Assert.Equal(RotationStatus.Unknown, meta.Status);
        Assert.Null(meta.LastRotatedAt);
        Assert.Null(meta.ExpiresAt);
    }

    [Fact]
    public void RotationStatus_AllValuesExist()
    {
        var values = Enum.GetValues<RotationStatus>();
        Assert.Contains(RotationStatus.Unknown,  values);
        Assert.Contains(RotationStatus.Current,  values);
        Assert.Contains(RotationStatus.DueSoon,  values);
        Assert.Contains(RotationStatus.Expired,  values);
    }
}

// ── Minimal logger capture ───────────────────────────────────────

file sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _records;

    public CapturingLogger(List<(LogLevel Level, string Message)> records)
        => _records = records;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel                         logLevel,
        EventId                          eventId,
        TState                           state,
        Exception?                       exception,
        Func<TState, Exception?, string> formatter)
        => _records.Add((logLevel, formatter(state, exception)));
}
