using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Application.Commands;
using OpsCopilot.AlertIngestion.Application.Handlers;
using OpsCopilot.AlertIngestion.Application.Normalizers;
using OpsCopilot.AlertIngestion.Application.Services;
using OpsCopilot.AlertIngestion.Domain.Models;
using OpsCopilot.AlertIngestion.Presentation.Contracts;
using OpsCopilot.AlertIngestion.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.AgentRuns;

namespace OpsCopilot.Modules.AlertIngestion.Tests;

/// <summary>
/// Tests for Slice 24 — AlertIngestion Hardening + Provider Normalization.
/// Covers normalizers, router, fingerprint, validation, and endpoint behaviour.
/// </summary>
public class AlertIngestionTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ════════════════════════════════════════════════════════════
    //  Helper: build valid Azure Monitor common alert schema JSON
    // ════════════════════════════════════════════════════════════

    private static string AzureMonitorPayload(
        string severity = "Sev1",
        string alertId = "alert-123",
        string alertRule = "HighCpu",
        string firedAt = "2025-06-01T12:00:00Z",
        string resourceId = "/subscriptions/abc/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1",
        string monitoringService = "Metric")
        => JsonSerializer.Serialize(new
        {
            data = new
            {
                essentials = new
                {
                    alertId,
                    alertRule,
                    severity,
                    firedDateTime = firedAt,
                    targetResourceIds = new[] { resourceId },
                    monitoringService,
                    description = "CPU > 90%",
                    targetResourceType = "Microsoft.Compute/virtualMachines",
                    monitorCondition = "Fired"
                }
            }
        });

    private static JsonElement Parse(string json)
        => JsonDocument.Parse(json).RootElement;

    // ════════════════════════════════════════════════════════════
    //  Helper: build valid Datadog webhook payload JSON
    // ════════════════════════════════════════════════════════════

    private static string DatadogPayload(
        string id = "dd-99",
        string title = "Disk Full",
        string priority = "p2",
        long dateHappened = 1717243200, // 2024-06-01T12:00:00Z
        string host = "web-01.prod",
        string alertType = "Metric",
        string[]? tags = null)
        => JsonSerializer.Serialize(new
        {
            id,
            title,
            body = "Disk usage above 95%",
            priority,
            date_happened = dateHappened,
            host,
            alert_type = alertType,
            tags = tags ?? new[] { "env:production", "team:ops" }
        });

    // ════════════════════════════════════════════════════════════
    //  Helper: generic payload JSON
    // ════════════════════════════════════════════════════════════

    private static string GenericPayload(
        string id = "gen-1",
        string title = "Generic Alert",
        string severity = "Warning",
        string resourceId = "host-42",
        string sourceType = "Event")
        => JsonSerializer.Serialize(new
        {
            id,
            title,
            severity,
            resourceId,
            sourceType,
            description = "Something happened"
        });

    // ════════════════════════════════════════════════════════════
    //  1. AzureMonitorAlertNormalizer — happy path
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void AzureMonitor_HappyPath_ReturnsNormalizedAlert()
    {
        var sut = new AzureMonitorAlertNormalizer();
        var json = AzureMonitorPayload();
        var result = sut.Normalize("azure_monitor", Parse(json));

        Assert.Equal("azure_monitor", result.Provider);
        Assert.Equal("alert-123", result.AlertExternalId);
        Assert.Equal("HighCpu", result.Title);
        Assert.Equal("Error", result.Severity); // Sev1 → Error
        Assert.Equal("CPU > 90%", result.Description);
        Assert.Contains("/virtualMachines/vm1", result.ResourceId);
        Assert.Equal("Metric", result.SourceType);
        Assert.NotNull(result.Dimensions);
        Assert.Equal("Fired", result.Dimensions!["monitorCondition"]);
        Assert.NotEmpty(result.RawPayload);
    }

    // ════════════════════════════════════════════════════════════
    //  2. AzureMonitorAlertNormalizer — severity mapping
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Sev0", "Critical")]
    [InlineData("Sev1", "Error")]
    [InlineData("Sev2", "Warning")]
    [InlineData("Sev3", "Informational")]
    [InlineData("Sev4", "Informational")]
    public void AzureMonitor_SeverityMapping_IsCorrect(string input, string expected)
    {
        var sut = new AzureMonitorAlertNormalizer();
        var json = AzureMonitorPayload(severity: input);
        var result = sut.Normalize("azure_monitor", Parse(json));

        Assert.Equal(expected, result.Severity);
    }

    // ════════════════════════════════════════════════════════════
    //  3. DatadogAlertNormalizer — happy path
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Datadog_HappyPath_ReturnsNormalizedAlert()
    {
        var sut = new DatadogAlertNormalizer();
        var json = DatadogPayload();
        var result = sut.Normalize("datadog", Parse(json));

        Assert.Equal("datadog", result.Provider);
        Assert.Equal("dd-99", result.AlertExternalId);
        Assert.Equal("Disk Full", result.Title);
        Assert.Equal("Error", result.Severity); // p2 → Error
        Assert.Equal("Disk usage above 95%", result.Description);
        Assert.Equal("web-01.prod", result.ResourceId);
        Assert.Equal("Metric", result.SourceType);
        Assert.NotEmpty(result.RawPayload);
    }

    // ════════════════════════════════════════════════════════════
    //  4. DatadogAlertNormalizer — priority mapping
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("p1", "Critical")]
    [InlineData("p2", "Error")]
    [InlineData("p3", "Warning")]
    [InlineData("p4", "Informational")]
    [InlineData("critical", "Critical")]
    [InlineData("high", "Error")]
    [InlineData("normal", "Warning")]
    [InlineData("low", "Informational")]
    public void Datadog_PriorityMapping_IsCorrect(string input, string expected)
    {
        var sut = new DatadogAlertNormalizer();
        var json = DatadogPayload(priority: input);
        var result = sut.Normalize("datadog", Parse(json));

        Assert.Equal(expected, result.Severity);
    }

    // ════════════════════════════════════════════════════════════
    //  5. DatadogAlertNormalizer — tags parsed to dimensions
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Datadog_Tags_ParsedToDimensionsDictionary()
    {
        var sut = new DatadogAlertNormalizer();
        var json = DatadogPayload(tags: new[] { "env:staging", "region:us-east-1", "no-colon-tag" });
        var result = sut.Normalize("datadog", Parse(json));

        Assert.NotNull(result.Dimensions);
        Assert.Equal(2, result.Dimensions!.Count); // no-colon-tag is skipped
        Assert.Equal("staging", result.Dimensions["env"]);
        Assert.Equal("us-east-1", result.Dimensions["region"]);
    }

    // ════════════════════════════════════════════════════════════
    //  6. GenericAlertNormalizer — happy path
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Generic_HappyPath_ReturnsNormalizedAlert()
    {
        var sut = new GenericAlertNormalizer();
        var json = GenericPayload();
        var result = sut.Normalize("custom_provider", Parse(json));

        Assert.Equal("custom_provider", result.Provider);
        Assert.Equal("gen-1", result.AlertExternalId);
        Assert.Equal("Generic Alert", result.Title);
        Assert.Equal("Warning", result.Severity);
        Assert.Equal("host-42", result.ResourceId);
        Assert.Equal("Event", result.SourceType);
        Assert.Equal("Something happened", result.Description);
        Assert.Null(result.Dimensions);
        Assert.NotEmpty(result.RawPayload);
    }

    // ════════════════════════════════════════════════════════════
    //  7. AlertNormalizerRouter — supported provider
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Router_SupportedProvider_ReturnsNormalizedAlert()
    {
        var router = BuildRouter();
        var json = AzureMonitorPayload();

        Assert.True(router.IsSupported("azure_monitor"));
        var result = router.Normalize("azure_monitor", Parse(json));
        Assert.Equal("azure_monitor", result.Provider);
    }

    // ════════════════════════════════════════════════════════════
    //  8. AlertNormalizerRouter — unsupported → IsSupported false
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Router_UnsupportedProvider_IsSupportedReturnsFalse()
    {
        var router = BuildRouter();
        Assert.False(router.IsSupported("splunk"));
    }

    // ════════════════════════════════════════════════════════════
    //  9. AlertNormalizerRouter — unsupported → throws
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Router_UnsupportedProvider_NormalizeThrowsInvalidOperation()
    {
        var router = BuildRouter();
        var json = "{}";
        var ex = Assert.Throws<InvalidOperationException>(
            () => router.Normalize("splunk", Parse(json)));

        Assert.Contains("splunk", ex.Message);
    }

    // ════════════════════════════════════════════════════════════
    //  10. Fingerprint — deterministic (same input → same hash)
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Fingerprint_SameNormalizedAlert_ReturnsSameHash()
    {
        var alert = MakeNormalizedAlert();

        var fp1 = NormalizedAlertFingerprintService.Compute(alert);
        var fp2 = NormalizedAlertFingerprintService.Compute(alert);

        Assert.Equal(fp1, fp2);
        Assert.Equal(64, fp1.Length); // SHA-256 hex = 64 chars
        Assert.Matches("^[0-9A-F]{64}$", fp1); // upper-case hex
    }

    // ════════════════════════════════════════════════════════════
    //  11. Fingerprint — different inputs → different hashes
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Fingerprint_DifferentInputs_ReturnDifferentHashes()
    {
        var alert1 = MakeNormalizedAlert(title: "HighCpu");
        var alert2 = MakeNormalizedAlert(title: "LowMemory");

        var fp1 = NormalizedAlertFingerprintService.Compute(alert1);
        var fp2 = NormalizedAlertFingerprintService.Compute(alert2);

        Assert.NotEqual(fp1, fp2);
    }

    // ════════════════════════════════════════════════════════════
    //  12. Validation — valid provider → IsValid true
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Validation_ValidProvider_IsValid()
    {
        var router = BuildRouter();
        var result = AlertValidationService.ValidateProvider("azure_monitor", router);

        Assert.True(result.IsValid);
        Assert.Null(result.ReasonCode);
    }

    // ════════════════════════════════════════════════════════════
    //  13. Validation — unsupported provider → frozen code
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Validation_UnsupportedProvider_ReturnsFrozenCode()
    {
        var router = BuildRouter();
        var result = AlertValidationService.ValidateProvider("splunk", router);

        Assert.False(result.IsValid);
        Assert.Equal("unsupported_provider", result.ReasonCode);
        Assert.Equal("The specified provider is not supported.", result.Message);
    }

    // ════════════════════════════════════════════════════════════
    //  14. Validation — null/empty payload → frozen code
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validation_EmptyPayload_ReturnsFrozenCode(string? payload)
    {
        var result = AlertValidationService.ValidatePayload(payload);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_alert_payload", result.ReasonCode);
        Assert.Equal("The alert payload could not be parsed.", result.Message);
    }

    // ════════════════════════════════════════════════════════════
    //  15. Endpoint — happy path → 200 OK with RunId + Fingerprint
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task Endpoint_HappyPath_Returns200WithRunIdAndFingerprint()
    {
        var (app, client, runCreator) = await CreateTestHost();
        try
        {
            var expectedRunId = Guid.NewGuid();
            runCreator
                .Setup(r => r.CreateRunAsync(
                    "tenant-24",
                    It.IsAny<string>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedRunId);

            var response = await Post(client, "tenant-24",
                new IngestAlertRequest("azure_monitor", AzureMonitorPayload()));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<IngestAlertResponse>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(expectedRunId, result!.RunId);
            Assert.Equal(64, result.Fingerprint.Length);
        }
        finally { await DisposeHost(app); }
    }

    // ════════════════════════════════════════════════════════════
    //  16. Endpoint — missing x-tenant-id → 400 missing_tenant
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task Endpoint_MissingTenant_Returns400MissingTenant()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await Post(client, tenantId: null,
                new IngestAlertRequest("azure_monitor", AzureMonitorPayload()));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<IngestAlertErrorResponse>(body, JsonOpts);
            Assert.NotNull(error);
            Assert.Equal("missing_tenant", error!.ReasonCode);
        }
        finally { await DisposeHost(app); }
    }

    // ════════════════════════════════════════════════════════════
    //  17. Endpoint — unsupported provider → 400 unsupported_provider
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task Endpoint_UnsupportedProvider_Returns400UnsupportedProvider()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await Post(client, "tenant-24",
                new IngestAlertRequest("splunk", GenericPayload()));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<IngestAlertErrorResponse>(body, JsonOpts);
            Assert.NotNull(error);
            Assert.Equal("unsupported_provider", error!.ReasonCode);
        }
        finally { await DisposeHost(app); }
    }

    // ════════════════════════════════════════════════════════════
    //  18. Endpoint — empty payload → 400 invalid_alert_payload
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task Endpoint_EmptyPayload_Returns400InvalidAlertPayload()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await Post(client, "tenant-24",
                new IngestAlertRequest("azure_monitor", ""));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<IngestAlertErrorResponse>(body, JsonOpts);
            Assert.NotNull(error);
            Assert.Equal("invalid_alert_payload", error!.ReasonCode);
        }
        finally { await DisposeHost(app); }
    }

    // ════════════════════════════════════════════════════════════
    //  Shared helpers
    // ════════════════════════════════════════════════════════════

    private static AlertNormalizerRouter BuildRouter()
        => new(new IAlertNormalizer[]
        {
            new AzureMonitorAlertNormalizer(),
            new DatadogAlertNormalizer(),
            new GenericAlertNormalizer()
        });

    private static NormalizedAlert MakeNormalizedAlert(
        string provider = "azure_monitor",
        string title = "HighCpu",
        string resourceId = "/subscriptions/abc/vm/vm1",
        string severity = "Error",
        string sourceType = "Metric")
        => new()
        {
            Provider = provider,
            AlertExternalId = "ext-1",
            Title = title,
            Severity = severity,
            FiredAtUtc = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            ResourceId = resourceId,
            SourceType = sourceType,
            RawPayload = "{}"
        };

    // ── Endpoint test host ──────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client, Mock<IAgentRunCreator> RunCreator)>
        CreateTestHost()
    {
        var runCreator = new Mock<IAgentRunCreator>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        // Register normalizers + router + handler
        builder.Services.AddSingleton<IAlertNormalizer, AzureMonitorAlertNormalizer>();
        builder.Services.AddSingleton<IAlertNormalizer, DatadogAlertNormalizer>();
        builder.Services.AddSingleton<IAlertNormalizer, GenericAlertNormalizer>();
        builder.Services.AddSingleton<AlertNormalizerRouter>();
        builder.Services.AddSingleton(runCreator.Object);
        builder.Services.AddScoped<IngestAlertCommandHandler>();

        var app = builder.Build();
        app.MapAlertIngestionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), runCreator);
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static async Task<HttpResponseMessage> Post(
        HttpClient client, string? tenantId, IngestAlertRequest request)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/ingest/alert")
        {
            Content = JsonContent.Create(request)
        };
        if (!string.IsNullOrEmpty(tenantId))
            msg.Headers.Add("x-tenant-id", tenantId);
        return await client.SendAsync(msg);
    }
}
