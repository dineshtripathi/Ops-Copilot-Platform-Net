using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Presentation.Endpoints;

namespace OpsCopilot.Modules.Reporting.Tests;

/// <summary>
/// HTTP-level tests for Slice 27: Platform Reporting Expansion — read-only
/// evaluation summary, connector inventory, and platform readiness reports.
/// </summary>
public class PlatformReportingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Test host factory ───────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client, Mock<IPlatformReportingQueryService> Svc)>
        CreateTestHost()
    {
        var svc = new Mock<IPlatformReportingQueryService>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(svc.Object);

        var app = builder.Build();
        app.MapPlatformReportingEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), svc);
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static HttpRequestMessage Get(string path)
        => new(HttpMethod.Get, path);

    private static EvaluationSummaryReport SampleEvalSummary(
        int total = 5, int passed = 4, int failed = 1)
        => new(total, passed, failed,
               total > 0 ? Math.Round((double)passed / total * 100, 2) : 0,
               new[] { "Governance", "SafeActions" },
               new[] { "Smoke", "Regression" },
               DateTime.UtcNow);

    private static ConnectorInventoryReport SampleConnectorInventory()
        => new(3,
               new Dictionary<string, int>
               {
                   ["Observability"] = 2,
                   ["Runbook"]       = 1
               },
               new List<ConnectorInventoryRow>
               {
                   new("azure-monitor", "Observability", "Azure Monitor connector", new[] { "query", "alerts" }),
                   new("datadog",       "Observability", "Datadog connector",       new[] { "query" }),
                   new("ansible",       "Runbook",       "Ansible connector",       new[] { "execute" })
               },
               DateTime.UtcNow);

    private static PlatformReadinessReport SampleReadiness(
        double passRate = 80.0, int connectors = 3, int actionTypes = 2,
        bool allPassing = false)
        => new(passRate, connectors, actionTypes, allPassing, DateTime.UtcNow);

    // ═══════════════════════════════════════════════════════════════
    // Evaluation Summary — /reports/platform/evaluation-summary
    // ═══════════════════════════════════════════════════════════════

    // ── 1. Evaluation summary — 200 OK with data ────────────────

    [Fact]
    public async Task EvaluationSummary_ReturnsOk_WithData()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleEvalSummary();
            svc.Setup(s => s.GetEvaluationSummaryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/evaluation-summary"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EvaluationSummaryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(5, result.TotalScenarios);
            Assert.Equal(4, result.Passed);
            Assert.Equal(1, result.Failed);
            svc.VerifyAll();
        }
        finally { await DisposeHost(app); }
    }

    // ── 2. Evaluation summary — 200 OK with zero scenarios ──────

    [Fact]
    public async Task EvaluationSummary_ReturnsOk_ZeroScenarios()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleEvalSummary(total: 0, passed: 0, failed: 0);
            svc.Setup(s => s.GetEvaluationSummaryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/evaluation-summary"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EvaluationSummaryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(0, result.TotalScenarios);
            Assert.Equal(0, result.PassRate);
        }
        finally { await DisposeHost(app); }
    }

    // ── 3. Evaluation summary — response shape validation ───────

    [Fact]
    public async Task EvaluationSummary_ResponseShape_ContainsAllFields()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleEvalSummary();
            svc.Setup(s => s.GetEvaluationSummaryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/evaluation-summary"));

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("totalScenarios", out _));
            Assert.True(root.TryGetProperty("passed",         out _));
            Assert.True(root.TryGetProperty("failed",         out _));
            Assert.True(root.TryGetProperty("passRate",       out _));
            Assert.True(root.TryGetProperty("modules",        out _));
            Assert.True(root.TryGetProperty("categories",     out _));
            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
        }
        finally { await DisposeHost(app); }
    }

    // ── 4. Evaluation summary — pass rate computation ───────────

    [Fact]
    public async Task EvaluationSummary_PassRateCorrect()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleEvalSummary(total: 10, passed: 7, failed: 3);
            svc.Setup(s => s.GetEvaluationSummaryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/evaluation-summary"));

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EvaluationSummaryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(70.0, result.PassRate);
        }
        finally { await DisposeHost(app); }
    }

    // ═══════════════════════════════════════════════════════════════
    // Connector Inventory — /reports/platform/connectors
    // ═══════════════════════════════════════════════════════════════

    // ── 5. Connectors — 200 OK with data ────────────────────────

    [Fact]
    public async Task Connectors_ReturnsOk_WithData()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleConnectorInventory();
            svc.Setup(s => s.GetConnectorInventoryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/connectors"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ConnectorInventoryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(3, result.TotalConnectors);
            Assert.Equal(3, result.Connectors.Count);
            svc.VerifyAll();
        }
        finally { await DisposeHost(app); }
    }

    // ── 6. Connectors — 200 OK empty list ───────────────────────

    [Fact]
    public async Task Connectors_ReturnsOk_EmptyList()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var empty = new ConnectorInventoryReport(
                0,
                new Dictionary<string, int>(),
                new List<ConnectorInventoryRow>(),
                DateTime.UtcNow);
            svc.Setup(s => s.GetConnectorInventoryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(empty);

            var response = await client.SendAsync(
                Get("/reports/platform/connectors"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ConnectorInventoryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(0, result.TotalConnectors);
            Assert.Empty(result.Connectors);
        }
        finally { await DisposeHost(app); }
    }

    // ── 7. Connectors — byKind grouping correct ────────────────

    [Fact]
    public async Task Connectors_ByKindGrouping_Correct()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleConnectorInventory();
            svc.Setup(s => s.GetConnectorInventoryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/connectors"));

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var byKind = doc.RootElement.GetProperty("byKind");
            Assert.Equal(2, byKind.GetProperty("Observability").GetInt32());
            Assert.Equal(1, byKind.GetProperty("Runbook").GetInt32());
        }
        finally { await DisposeHost(app); }
    }

    // ── 8. Connectors — response shape validation ───────────────

    [Fact]
    public async Task Connectors_ResponseShape_ContainsAllFields()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleConnectorInventory();
            svc.Setup(s => s.GetConnectorInventoryAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/connectors"));

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("totalConnectors", out _));
            Assert.True(root.TryGetProperty("byKind",          out _));
            Assert.True(root.TryGetProperty("connectors",      out _));
            Assert.True(root.TryGetProperty("generatedAtUtc",  out _));

            // Verify row shape
            var firstRow = root.GetProperty("connectors")[0];
            Assert.True(firstRow.TryGetProperty("name",         out _));
            Assert.True(firstRow.TryGetProperty("kind",         out _));
            Assert.True(firstRow.TryGetProperty("description",  out _));
            Assert.True(firstRow.TryGetProperty("capabilities", out _));
        }
        finally { await DisposeHost(app); }
    }

    // ═══════════════════════════════════════════════════════════════
    // Platform Readiness — /reports/platform/readiness
    // ═══════════════════════════════════════════════════════════════

    // ── 9. Readiness — 200 OK with data ─────────────────────────

    [Fact]
    public async Task Readiness_ReturnsOk_WithData()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleReadiness();
            svc.Setup(s => s.GetPlatformReadinessAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/readiness"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PlatformReadinessReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(80.0, result.EvaluationPassRate);
            Assert.Equal(3, result.TotalConnectors);
            Assert.Equal(2, result.TotalActionTypes);
            svc.VerifyAll();
        }
        finally { await DisposeHost(app); }
    }

    // ── 10. Readiness — allPassing true ─────────────────────────

    [Fact]
    public async Task Readiness_AllPassing_True()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleReadiness(
                passRate: 100.0, connectors: 5, actionTypes: 3, allPassing: true);
            svc.Setup(s => s.GetPlatformReadinessAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/readiness"));

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PlatformReadinessReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.True(result.AllEvaluationsPassing);
            Assert.Equal(100.0, result.EvaluationPassRate);
        }
        finally { await DisposeHost(app); }
    }

    // ── 11. Readiness — allPassing false (failures) ─────────────

    [Fact]
    public async Task Readiness_AllPassing_False_WhenFailures()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleReadiness(
                passRate: 60.0, connectors: 2, actionTypes: 1, allPassing: false);
            svc.Setup(s => s.GetPlatformReadinessAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/readiness"));

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PlatformReadinessReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.False(result.AllEvaluationsPassing);
            Assert.Equal(60.0, result.EvaluationPassRate);
        }
        finally { await DisposeHost(app); }
    }

    // ── 12. Readiness — allPassing false (zero scenarios) ───────

    [Fact]
    public async Task Readiness_AllPassing_False_WhenZeroScenarios()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleReadiness(
                passRate: 0.0, connectors: 1, actionTypes: 0, allPassing: false);
            svc.Setup(s => s.GetPlatformReadinessAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/readiness"));

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PlatformReadinessReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.False(result.AllEvaluationsPassing);
            Assert.Equal(0.0, result.EvaluationPassRate);
        }
        finally { await DisposeHost(app); }
    }

    // ── 13. Readiness — response shape validation ───────────────

    [Fact]
    public async Task Readiness_ResponseShape_ContainsAllFields()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = SampleReadiness();
            svc.Setup(s => s.GetPlatformReadinessAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/platform/readiness"));

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("evaluationPassRate",    out _));
            Assert.True(root.TryGetProperty("totalConnectors",      out _));
            Assert.True(root.TryGetProperty("totalActionTypes",     out _));
            Assert.True(root.TryGetProperty("allEvaluationsPassing", out _));
            Assert.True(root.TryGetProperty("generatedAtUtc",       out _));
        }
        finally { await DisposeHost(app); }
    }
}
