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
/// Integration tests for the Slice 35 catalog query endpoints.
/// Uses TestHost + Moq following the same pattern as
/// <see cref="PlatformPacksEndpointTests"/>.
/// </summary>
public class PlatformPacksQueryEndpointTests : IDisposable
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
        bool isValid = true,
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

        var validation = new PackValidationResult(isValid, Array.Empty<string>());
        return new LoadedPack(manifest, $"/packs/{name}", validation);
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /reports/platform/packs/{name} — pack details
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPackDetails_Existing_ReturnsOkWithDetails()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var details = new PackDetails(
                Name: "azure-vm",
                Version: "1.0.0",
                Description: "Test pack.",
                ResourceTypes: new[] { "Microsoft.Compute/virtualMachines" },
                MinimumMode: "A",
                EvidenceCollectorCount: 1,
                RunbookCount: 2,
                SafeActionCount: 0,
                IsValid: true,
                Errors: Array.Empty<string>(),
                PackPath: "/packs/azure-vm");

            catalog.Setup(c => c.GetDetailsAsync("azure-vm", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(details);

            var response = await client.GetAsync("/reports/platform/packs/azure-vm");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            var pack = root.GetProperty("pack");
            Assert.Equal("azure-vm", pack.GetProperty("name").GetString());
            Assert.Equal("1.0.0", pack.GetProperty("version").GetString());
            Assert.Equal("A", pack.GetProperty("minimumMode").GetString());
            Assert.Equal(1, pack.GetProperty("evidenceCollectorCount").GetInt32());
            Assert.Equal(2, pack.GetProperty("runbookCount").GetInt32());
            Assert.Equal(0, pack.GetProperty("safeActionCount").GetInt32());
            Assert.True(pack.GetProperty("isValid").GetBoolean());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    [Fact]
    public async Task GetPackDetails_NotFound_Returns404()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            catalog.Setup(c => c.GetDetailsAsync("ghost", It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PackDetails?)null);

            var response = await client.GetAsync("/reports/platform/packs/ghost");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Contains("not found", doc.RootElement.GetProperty("error").GetString());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /reports/platform/packs/search — search
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchPacks_ByResourceType_ReturnsFiltered()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var packs = new List<LoadedPack>
            {
                MakeLoadedPack("azure-vm")
            };
            catalog.Setup(c => c.FindByResourceTypeAsync(
                       "Microsoft.Compute/virtualMachines", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(packs);

            var response = await client.GetAsync(
                "/reports/platform/packs/search?resourceType=Microsoft.Compute/virtualMachines");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
            var packsArr = root.GetProperty("packs");
            Assert.Equal(1, packsArr.GetArrayLength());
            Assert.Equal("azure-vm", packsArr[0].GetProperty("name").GetString());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    [Fact]
    public async Task SearchPacks_ByMinimumMode_ReturnsFiltered()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var packs = new List<LoadedPack>
            {
                MakeLoadedPack("pack-b", minimumMode: "B")
            };
            catalog.Setup(c => c.FindByMinimumModeAsync("B", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(packs);

            var response = await client.GetAsync(
                "/reports/platform/packs/search?minimumMode=B");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("totalResults").GetInt32());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    [Fact]
    public async Task SearchPacks_NoFilters_ReturnsAll()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var packs = new List<LoadedPack>
            {
                MakeLoadedPack("azure-vm"),
                MakeLoadedPack("k8s-basic")
            };
            catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(packs);

            var response = await client.GetAsync("/reports/platform/packs/search");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(2, doc.RootElement.GetProperty("totalResults").GetInt32());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /reports/platform/packs/{name}/runbooks
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPackRunbooks_Existing_ReturnsRunbookList()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var runbooks = new List<PackRunbookSummary>
            {
                new("restart-vm", "runbooks/restart.md"),
                new("check-health", "runbooks/health.md")
            };
            catalog.Setup(c => c.GetRunbooksAsync("azure-vm", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(runbooks);

            var response = await client.GetAsync("/reports/platform/packs/azure-vm/runbooks");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            Assert.Equal("azure-vm", root.GetProperty("packName").GetString());
            Assert.Equal(2, root.GetProperty("totalRunbooks").GetInt32());
            var arr = root.GetProperty("runbooks");
            Assert.Equal(2, arr.GetArrayLength());
            Assert.Equal("restart-vm", arr[0].GetProperty("id").GetString());
            Assert.Equal("runbooks/restart.md", arr[0].GetProperty("file").GetString());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    [Fact]
    public async Task GetPackRunbooks_NotFound_Returns404()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            catalog.Setup(c => c.GetRunbooksAsync("ghost", It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<PackRunbookSummary>?)null);

            var response = await client.GetAsync("/reports/platform/packs/ghost/runbooks");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /reports/platform/packs/{name}/evidence-collectors
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPackEvidenceCollectors_Existing_ReturnsList()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var collectors = new List<PackEvidenceCollectorSummary>
            {
                new("cpu-check", "A", "queries/cpu.kql")
            };
            catalog.Setup(c => c.GetEvidenceCollectorsAsync("azure-vm", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(collectors);

            var response = await client.GetAsync(
                "/reports/platform/packs/azure-vm/evidence-collectors");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            Assert.Equal("azure-vm", root.GetProperty("packName").GetString());
            Assert.Equal(1, root.GetProperty("totalEvidenceCollectors").GetInt32());
            var arr = root.GetProperty("evidenceCollectors");
            Assert.Equal("cpu-check", arr[0].GetProperty("id").GetString());
            Assert.Equal("A", arr[0].GetProperty("requiredMode").GetString());
            Assert.Equal("queries/cpu.kql", arr[0].GetProperty("queryFile").GetString());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    [Fact]
    public async Task GetPackEvidenceCollectors_NotFound_Returns404()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            catalog.Setup(c => c.GetEvidenceCollectorsAsync("ghost", It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<PackEvidenceCollectorSummary>?)null);

            var response = await client.GetAsync(
                "/reports/platform/packs/ghost/evidence-collectors");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /reports/platform/packs/{name}/safe-actions
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPackSafeActions_Existing_ReturnsList()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            var actions = new List<PackSafeActionSummary>
            {
                new("restart", "C", "actions/restart.json")
            };
            catalog.Setup(c => c.GetSafeActionsAsync("azure-vm", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(actions);

            var response = await client.GetAsync(
                "/reports/platform/packs/azure-vm/safe-actions");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            Assert.Equal("azure-vm", root.GetProperty("packName").GetString());
            Assert.Equal(1, root.GetProperty("totalSafeActions").GetInt32());
            var arr = root.GetProperty("safeActions");
            Assert.Equal("restart", arr[0].GetProperty("id").GetString());
            Assert.Equal("C", arr[0].GetProperty("requiresMode").GetString());
            Assert.Equal("actions/restart.json", arr[0].GetProperty("definitionFile").GetString());

            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }

    [Fact]
    public async Task GetPackSafeActions_NotFound_Returns404()
    {
        var (app, client, catalog) = await CreateTestHostAsync();
        try
        {
            catalog.Setup(c => c.GetSafeActionsAsync("ghost", It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<PackSafeActionSummary>?)null);

            var response = await client.GetAsync(
                "/reports/platform/packs/ghost/safe-actions");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            catalog.VerifyAll();
        }
        finally
        {
            DisposeHost(app);
            _app = null;
        }
    }
}
