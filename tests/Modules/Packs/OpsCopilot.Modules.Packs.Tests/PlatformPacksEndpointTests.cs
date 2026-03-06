using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Presentation.Endpoints;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Integration tests for <c>GET /reports/platform/packs</c>.
/// Uses TestHost + Moq following the same pattern as
/// PlatformReportingEndpointTests.
/// </summary>
public class PlatformPacksEndpointTests : IDisposable
{
    private WebApplication? _app;
    private HttpClient? _client;

    // ── Test-host factory ──────────────────────────────────────

    private async Task<(WebApplication App, HttpClient Client, Mock<IPackCatalog> Catalog)>
        CreateTestHostAsync()
    {
        var catalog = new Mock<IPackCatalog>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(catalog.Object);

        var app = builder.Build();
        app.MapPlatformPacksEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();
        _app = app;
        _client = client;

        return (app, client, catalog);
    }

    private static void DisposeHost(WebApplication app)
    {
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client?.Dispose();
        if (_app is not null) DisposeHost(_app);
    }

    // ── Helpers ────────────────────────────────────────────────

    private static LoadedPack MakeLoadedPack(
        string name,
        bool isValid,
        string[]? errors = null,
        string minimumMode = "A",
        string[]? resourceTypes = null,
        IReadOnlyList<EvidenceCollector>? evidenceCollectors = null,
        IReadOnlyList<PackRunbook>? runbooks = null,
        IReadOnlyList<PackSafeAction>? safeActions = null)
    {
        var manifest = new PackManifest(
            Name: name,
            Version: "1.0.0",
            Description: "Test pack.",
            ResourceTypes: resourceTypes ?? new[] { "Microsoft.Compute/virtualMachines" },
            MinimumMode: minimumMode,
            EvidenceCollectors: evidenceCollectors ?? Array.Empty<EvidenceCollector>(),
            Runbooks: runbooks ?? Array.Empty<PackRunbook>(),
            SafeActions: safeActions ?? Array.Empty<PackSafeAction>());

        var validation = new PackValidationResult(isValid, errors ?? Array.Empty<string>());
        return new LoadedPack(manifest, $"/packs/{name}", validation);
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. Happy path — packs returned with correct shape
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPacks_ReturnsOkWithCorrectShape()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var packs = new List<LoadedPack>
            {
                MakeLoadedPack("azure-vm", isValid: true,
                    resourceTypes: new[] { "Microsoft.Compute/virtualMachines" },
                    evidenceCollectors: new[] { new EvidenceCollector("cpu-check", "A", null) },
                    runbooks: new[] { new PackRunbook("restart-vm", "runbooks/restart.md") },
                    safeActions: new[] { new PackSafeAction("restart", "C", null) }),
                MakeLoadedPack("k8s-basic", isValid: true, minimumMode: "B")
            };
            catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(packs);

            var response = await client.GetAsync("/reports/platform/packs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Top-level envelope
            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            Assert.Equal(2, root.GetProperty("totalPacks").GetInt32());
            Assert.Equal(2, root.GetProperty("validPacks").GetInt32());
            Assert.Equal(0, root.GetProperty("invalidPacks").GetInt32());

            // Packs array
            var packsArr = root.GetProperty("packs");
            Assert.Equal(2, packsArr.GetArrayLength());

            // First pack — azure-vm  (has sub-items)
            var first = packsArr[0];
            Assert.Equal("azure-vm", first.GetProperty("name").GetString());
            Assert.Equal("1.0.0", first.GetProperty("version").GetString());
            Assert.Equal("A", first.GetProperty("minimumMode").GetString());
            Assert.Equal(1, first.GetProperty("resourceTypesCount").GetInt32());
            Assert.Equal(1, first.GetProperty("evidenceCollectorsCount").GetInt32());
            Assert.Equal(1, first.GetProperty("runbooksCount").GetInt32());
            Assert.Equal(1, first.GetProperty("safeActionsCount").GetInt32());
            Assert.True(first.GetProperty("isValid").GetBoolean());
            Assert.Equal(0, first.GetProperty("errors").GetArrayLength());

            // Second pack — k8s-basic  (empty sub-items)
            var second = packsArr[1];
            Assert.Equal("k8s-basic", second.GetProperty("name").GetString());
            Assert.Equal("B", second.GetProperty("minimumMode").GetString());
            Assert.Equal(0, second.GetProperty("evidenceCollectorsCount").GetInt32());
            Assert.Equal(0, second.GetProperty("runbooksCount").GetInt32());
            Assert.Equal(0, second.GetProperty("safeActionsCount").GetInt32());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Empty catalog — returns zeros
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPacks_EmptyCatalog_ReturnsZeros()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<LoadedPack>());

            var response = await client.GetAsync("/reports/platform/packs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            Assert.Equal(0, root.GetProperty("totalPacks").GetInt32());
            Assert.Equal(0, root.GetProperty("validPacks").GetInt32());
            Assert.Equal(0, root.GetProperty("invalidPacks").GetInt32());
            Assert.Equal(0, root.GetProperty("packs").GetArrayLength());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Mixed valid/invalid packs — correct counts & errors
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPacks_MixedValidity_ReturnsCorrectCounts()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var packs = new List<LoadedPack>
            {
                MakeLoadedPack("azure-vm", isValid: true),
                MakeLoadedPack("broken-pack", isValid: false,
                    errors: new[] { "'version' is required." })
            };
            catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(packs);

            var response = await client.GetAsync("/reports/platform/packs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(2, root.GetProperty("totalPacks").GetInt32());
            Assert.Equal(1, root.GetProperty("validPacks").GetInt32());
            Assert.Equal(1, root.GetProperty("invalidPacks").GetInt32());

            // The invalid pack should carry its errors through
            var packsArr = root.GetProperty("packs");
            var invalidPack = packsArr.EnumerateArray()
                .First(p => !p.GetProperty("isValid").GetBoolean());
            Assert.Equal("broken-pack", invalidPack.GetProperty("name").GetString());
            Assert.Equal(1, invalidPack.GetProperty("errors").GetArrayLength());
            Assert.Equal("'version' is required.",
                invalidPack.GetProperty("errors")[0].GetString());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }
}
