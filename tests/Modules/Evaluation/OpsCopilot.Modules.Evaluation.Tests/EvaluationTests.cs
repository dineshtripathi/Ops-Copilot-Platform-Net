using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Application.Services;
using OpsCopilot.Evaluation.Domain.Models;
using OpsCopilot.Evaluation.Presentation.Endpoints;
using OpsCopilot.Evaluation.Presentation.Extensions;

namespace OpsCopilot.Modules.Evaluation.Tests;

/// <summary>
/// Tests for Slice 25: Evaluation MVP — deterministic, no persistence.
/// </summary>
public class EvaluationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Test host factory ───────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client)>
        CreateTestHost()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddEvaluationModule();

        var app = builder.Build();
        app.MapEvaluationEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ── 1. Catalog has at least 10 scenarios (AC-3) ─────────────

    [Fact]
    public void Catalog_ContainsAtLeast10Scenarios()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var catalog = sp.GetRequiredService<EvaluationScenarioCatalog>();
        Assert.True(catalog.Scenarios.Count >= 10,
            $"Expected ≥10 scenarios, got {catalog.Scenarios.Count}");
    }

    // ── 2. Catalog covers AlertIngestion (≥3) (AC-4) ────────────

    [Fact]
    public void Catalog_HasAtLeast3AlertIngestionScenarios()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var catalog = sp.GetRequiredService<EvaluationScenarioCatalog>();
        var count = catalog.Scenarios.Count(s => s.Module == "AlertIngestion");
        Assert.True(count >= 3,
            $"Expected ≥3 AlertIngestion scenarios, got {count}");
    }

    // ── 3. Catalog covers SafeActions (≥3) (AC-4) ───────────────

    [Fact]
    public void Catalog_HasAtLeast3SafeActionsScenarios()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var catalog = sp.GetRequiredService<EvaluationScenarioCatalog>();
        var count = catalog.Scenarios.Count(s => s.Module == "SafeActions");
        Assert.True(count >= 3,
            $"Expected ≥3 SafeActions scenarios, got {count}");
    }

    // ── 4. Catalog covers Reporting (≥2) (AC-4) ─────────────────

    [Fact]
    public void Catalog_HasAtLeast2ReportingScenarios()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var catalog = sp.GetRequiredService<EvaluationScenarioCatalog>();
        var count = catalog.Scenarios.Count(s => s.Module == "Reporting");
        Assert.True(count >= 2,
            $"Expected ≥2 Reporting scenarios, got {count}");
    }

    // ── 5. Runner returns correct summary (AC-2, AC-5) ──────────

    [Fact]
    public void Runner_ReturnsCorrectSummary()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<EvaluationRunner>();
        var summary = runner.Run();

        Assert.True(summary.TotalScenarios >= 10);
        Assert.Equal(summary.Results.Count, summary.TotalScenarios);
        Assert.Equal(summary.Passed + summary.Failed, summary.TotalScenarios);
    }

    // ── 6. All built-in scenarios pass (AC-6) ───────────────────

    [Fact]
    public void Runner_AllScenariosPass()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<EvaluationRunner>();
        var summary = runner.Run();

        Assert.Equal(0, summary.Failed);
        Assert.Equal(summary.TotalScenarios, summary.Passed);
        foreach (var r in summary.Results)
            Assert.True(r.Passed, $"{r.ScenarioId} failed: Expected={r.Expected}, Actual={r.Actual}, Reason={r.Reason}");
    }

    // ── 7. Runner is deterministic (AC-1) ───────────────────────

    [Fact]
    public void Runner_IsDeterministic_AcrossRuns()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<EvaluationRunner>();
        var run1 = runner.Run();
        var run2 = runner.Run();

        Assert.Equal(run1.TotalScenarios, run2.TotalScenarios);
        Assert.Equal(run1.Passed, run2.Passed);
        Assert.Equal(run1.Failed, run2.Failed);

        for (int i = 0; i < run1.Results.Count; i++)
        {
            Assert.Equal(run1.Results[i].ScenarioId, run2.Results[i].ScenarioId);
            Assert.Equal(run1.Results[i].Passed, run2.Results[i].Passed);
            Assert.Equal(run1.Results[i].Expected, run2.Results[i].Expected);
            Assert.Equal(run1.Results[i].Actual, run2.Results[i].Actual);
        }
    }

    // ── 8. Each RunId is unique (AC-2) ──────────────────────────

    [Fact]
    public void Runner_EachRunIdIsUnique()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<EvaluationRunner>();
        var id1 = runner.Run().RunId;
        var id2 = runner.Run().RunId;

        Assert.NotEqual(id1, id2);
    }

    // ── 9. GET /evaluation/run returns 200 (AC-7) ───────────────

    [Fact]
    public async Task GetRun_Returns200()
    {
        var (app, client) = await CreateTestHost();
        try
        {
            var response = await client.GetAsync("/evaluation/run");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 10. GET /evaluation/run returns valid JSON (AC-7) ───────

    [Fact]
    public async Task GetRun_ReturnsValidEvaluationRunSummary()
    {
        var (app, client) = await CreateTestHost();
        try
        {
            var response = await client.GetAsync("/evaluation/run");
            var body = await response.Content.ReadAsStringAsync();
            var summary = JsonSerializer.Deserialize<EvaluationRunSummary>(body, JsonOpts);

            Assert.NotNull(summary);
            Assert.NotEqual(Guid.Empty, summary.RunId);
            Assert.True(summary.TotalScenarios >= 10);
            Assert.Equal(summary.TotalScenarios, summary.Passed + summary.Failed);
            Assert.NotNull(summary.Results);
            Assert.Equal(summary.TotalScenarios, summary.Results.Count);
        }
        finally { await DisposeHost(app); }
    }

    // ── 11. GET /evaluation/scenarios returns 200 (AC-8) ────────

    [Fact]
    public async Task GetScenarios_Returns200()
    {
        var (app, client) = await CreateTestHost();
        try
        {
            var response = await client.GetAsync("/evaluation/scenarios");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 12. GET /evaluation/scenarios returns scenario list ─────

    [Fact]
    public async Task GetScenarios_ReturnsListOfScenarioMetadata()
    {
        var (app, client) = await CreateTestHost();
        try
        {
            var response = await client.GetAsync("/evaluation/scenarios");
            var body = await response.Content.ReadAsStringAsync();
            var scenarios = JsonSerializer.Deserialize<List<EvaluationScenario>>(body, JsonOpts);

            Assert.NotNull(scenarios);
            Assert.True(scenarios.Count >= 10);
            Assert.All(scenarios, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.ScenarioId));
                Assert.False(string.IsNullOrWhiteSpace(s.Module));
                Assert.False(string.IsNullOrWhiteSpace(s.Name));
            });
        }
        finally { await DisposeHost(app); }
    }

    // ── 13. Each scenario has unique ScenarioId ─────────────────

    [Fact]
    public void AllScenarioIds_AreUnique()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var catalog = sp.GetRequiredService<EvaluationScenarioCatalog>();
        var ids = catalog.Scenarios.Select(s => s.ScenarioId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // ── 14. Individual scenario Execute returns correct id ──────

    [Fact]
    public void EachScenario_Execute_ReturnsMatchingScenarioId()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var scenarios = sp.GetServices<IEvaluationScenario>().ToList();
        foreach (var scenario in scenarios)
        {
            var result = scenario.Execute();
            Assert.Equal(scenario.ScenarioId, result.ScenarioId);
            Assert.Equal(scenario.Module, result.Module);
        }
    }

    // ── 15. GetMetadata projects correctly ──────────────────────

    [Fact]
    public void Catalog_GetMetadata_ProjectsCorrectly()
    {
        var services = new ServiceCollection();
        services.AddEvaluationModule();
        using var sp = services.BuildServiceProvider();

        var catalog = sp.GetRequiredService<EvaluationScenarioCatalog>();
        var metadata = catalog.GetMetadata();

        Assert.Equal(catalog.Scenarios.Count, metadata.Count);
        foreach (var m in metadata)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.ScenarioId));
            Assert.False(string.IsNullOrWhiteSpace(m.Module));
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.False(string.IsNullOrWhiteSpace(m.Category));
            Assert.False(string.IsNullOrWhiteSpace(m.Description));
        }
    }
}
