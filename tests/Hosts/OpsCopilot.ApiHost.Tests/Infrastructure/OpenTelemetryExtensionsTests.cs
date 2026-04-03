using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.ApiHost.Infrastructure;
using Xunit;

namespace OpsCopilot.ApiHost.Tests.Infrastructure;

/// <summary>
/// Slice 152 — Unit tests for OpenTelemetry DI registration.
/// Verifies service container shape and config-binding without
/// requiring a running OTLP collector.
/// </summary>
public sealed class OpenTelemetryExtensionsTests
{
    // ── Service registration ──────────────────────────────────────────────────

    [Fact]
    public void AddOpsCopilotOpenTelemetry_DefaultConfig_DoesNotThrow()
    {
        // Arrange — no Telemetry section; console exporter should be used
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert
        var ex = Record.Exception(() => services.AddOpsCopilotOpenTelemetry(config));
        Assert.Null(ex);
    }

    [Fact]
    public void AddOpsCopilotOpenTelemetry_EmptyEndpoint_DoesNotThrow()
    {
        // Arrange — explicit empty endpoint; console exporter should be used
        var config = BuildConfig(otlpEndpoint: "");
        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert
        var ex = Record.Exception(() => services.AddOpsCopilotOpenTelemetry(config));
        Assert.Null(ex);
    }

    [Fact]
    public void AddOpsCopilotOpenTelemetry_OtlpEndpointSet_DoesNotThrow()
    {
        // Arrange — OTLP endpoint configured; OTLP exporter should be wired
        var config = BuildConfig(otlpEndpoint: "http://localhost:4317");
        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert
        var ex = Record.Exception(() => services.AddOpsCopilotOpenTelemetry(config));
        Assert.Null(ex);
    }

    [Fact]
    public void AddOpsCopilotOpenTelemetry_BuildsServiceProvider()
    {
        // Arrange
        var config = BuildConfig(otlpEndpoint: "");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpsCopilotOpenTelemetry(config);

        // Act + Assert — full container build succeeds
        var ex = Record.Exception(() =>
        {
            using var sp = services.BuildServiceProvider();
        });
        Assert.Null(ex);
    }

    // ── Source / meter arrays ─────────────────────────────────────────────────

    [Fact]
    public void TraceSources_ContainsExpectedSources()
    {
        Assert.Contains("OpsCopilot.Triage", OpenTelemetryExtensions.TraceSources);
        Assert.Contains("OpsCopilot.SafeActions", OpenTelemetryExtensions.TraceSources);
    }

    [Fact]
    public void MeterNames_ContainsExpectedMeters()
    {
        Assert.Contains("OpsCopilot.Triage", OpenTelemetryExtensions.MeterNames);
        Assert.Contains("OpsCopilot.SafeActions", OpenTelemetryExtensions.MeterNames);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(string? otlpEndpoint = null)
    {
        var data = new Dictionary<string, string?>();

        if (otlpEndpoint is not null)
            data["Telemetry:OtlpEndpoint"] = otlpEndpoint;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }
}
