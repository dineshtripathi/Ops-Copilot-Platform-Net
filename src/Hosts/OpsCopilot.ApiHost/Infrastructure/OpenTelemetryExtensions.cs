using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OpsCopilot.ApiHost.Infrastructure;

/// <summary>
/// Slice 152 — Registers OpenTelemetry distributed tracing and metrics.
///
/// ActivitySources:
///   • OpsCopilot.Triage       — spans for triage orchestration
///   • OpsCopilot.SafeActions  — spans for safe-action lifecycle
///
/// Meters:
///   • OpsCopilot.Triage       — triage run count + latency histogram
///   • OpsCopilot.SafeActions  — policy denial counter
///
/// Exporter selection:
///   • Telemetry:OtlpEndpoint set → OTLP gRPC exporter (Jaeger / Aspire Dashboard / OTEL Collector)
///   • Telemetry:OtlpEndpoint empty or missing → Console exporter (dev-safe default)
/// </summary>
internal static class OpenTelemetryExtensions
{
    internal static readonly string[] TraceSources =
    [
        "OpsCopilot.Triage",
        "OpsCopilot.SafeActions"
    ];

    internal static readonly string[] MeterNames =
    [
        "OpsCopilot.Triage",
        "OpsCopilot.SafeActions"
    ];

    internal static IServiceCollection AddOpsCopilotOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Telemetry:OtlpEndpoint"];

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("OpsCopilot.ApiHost"));

        otel.WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation();
            tracing.AddHttpClientInstrumentation(); // Slice 163: trace outbound HTTP calls (Azure, MCP, downstream APIs)

            foreach (var source in TraceSources)
                tracing.AddSource(source);

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            else
                tracing.AddConsoleExporter();
        });

        otel.WithMetrics(metrics =>
        {
            foreach (var meter in MeterNames)
                metrics.AddMeter(meter);

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            else
                metrics.AddConsoleExporter();
        });

        return services;
    }
}
