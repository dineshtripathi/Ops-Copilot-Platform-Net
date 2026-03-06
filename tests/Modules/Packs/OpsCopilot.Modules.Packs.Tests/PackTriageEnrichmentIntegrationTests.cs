using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Integration tests wiring the full Slice 36 enrichment pipeline with real filesystem:
/// FileSystemPackLoader → PackCatalog → PackFileReader → PackTriageEnricher.
/// No mocks — validates the end-to-end Pack → Triage enrichment flow.
/// </summary>
public sealed class PackTriageEnrichmentIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public PackTriageEnrichmentIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "packs-integ-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private PackTriageEnricher CreateEnricher()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Packs:RootPath"] = _tempRoot
            })
            .Build();

        var loader = new FileSystemPackLoader(config, NullLogger<FileSystemPackLoader>.Instance);
        var catalog = new PackCatalog(loader);
        var fileReader = new PackFileReader(NullLogger<PackFileReader>.Instance);

        return new PackTriageEnricher(catalog, fileReader, NullLogger<PackTriageEnricher>.Instance);
    }

    private string CreatePackDirectory(string packName)
    {
        var dir = Path.Combine(_tempRoot, packName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePackJson(string packDir, PackManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(Path.Combine(packDir, "pack.json"), json);
    }

    private static void WriteFile(string packDir, string relativePath, string content)
    {
        var fullPath = Path.Combine(packDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static PackManifest MakeManifest(
        string name,
        string minimumMode = "A",
        IReadOnlyList<EvidenceCollector>? evidenceCollectors = null,
        IReadOnlyList<PackRunbook>? runbooks = null,
        IReadOnlyList<PackSafeAction>? safeActions = null) =>
        new(
            name,
            "1.0.0",
            $"Pack {name} description.",
            new[] { "Microsoft.Compute/virtualMachines" },
            minimumMode,
            evidenceCollectors ?? Array.Empty<EvidenceCollector>(),
            runbooks ?? Array.Empty<PackRunbook>(),
            safeActions ?? Array.Empty<PackSafeAction>());

    // ═══════════════════════════════════════════════════════════════
    // 1. Happy path — runbook + Mode-A collector both returned
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_PackWithRunbookAndModeACollector_ReturnsEnrichment()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "runbooks/restart.md", "# Restart\nRestart the VM gracefully.");
        WriteFile(dir, "queries/cpu.kql", "Perf | where CounterName == 'cpu'");
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            runbooks: new[] { new PackRunbook("restart-vm", "runbooks/restart.md") },
            evidenceCollectors: new[] { new EvidenceCollector("cpu-usage", "A", "queries/cpu.kql") }));

        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackErrors);

        Assert.Single(result.PackRunbooks);
        var rb = result.PackRunbooks[0];
        Assert.Equal("azure-vm", rb.PackName);
        Assert.Equal("restart-vm", rb.RunbookId);
        Assert.Equal("runbooks/restart.md", rb.File);
        Assert.Contains("Restart the VM gracefully", rb.ContentSnippet);

        Assert.Single(result.PackEvidenceCollectors);
        var ec = result.PackEvidenceCollectors[0];
        Assert.Equal("azure-vm", ec.PackName);
        Assert.Equal("cpu-usage", ec.EvidenceCollectorId);
        Assert.Equal("A", ec.RequiredMode);
        Assert.Contains("Perf | where CounterName", ec.KqlContent);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Mode-B collector is filtered out
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ModeBOnlyCollector_IsFilteredOut()
    {
        var dir = CreatePackDirectory("net-monitor");
        WriteFile(dir, "queries/latency.kql", "AppRequests | where Latency > 5000");
        WritePackJson(dir, MakeManifest(
            "net-monitor",
            minimumMode: "A",
            evidenceCollectors: new[] { new EvidenceCollector("latency-check", "B", "queries/latency.kql") }));

        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackErrors);
        Assert.Empty(result.PackRunbooks);
        Assert.Empty(result.PackEvidenceCollectors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Invalid pack manifest is skipped (name mismatch → IsValid=false)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_InvalidPackManifest_IsSkipped()
    {
        // Name "wrong-name" doesn't match directory "bad-pack" → validation fails
        var dir = CreatePackDirectory("bad-pack");
        WriteFile(dir, "runbooks/x.md", "Should not appear.");
        WritePackJson(dir, MakeManifest(
            "wrong-name",
            minimumMode: "A",
            runbooks: new[] { new PackRunbook("some-rb", "runbooks/x.md") }));

        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackRunbooks);
        Assert.Empty(result.PackEvidenceCollectors);
        Assert.Empty(result.PackErrors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Empty packs directory → empty enrichment
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_EmptyPacksDirectory_ReturnsEmptyEnrichment()
    {
        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackRunbooks);
        Assert.Empty(result.PackEvidenceCollectors);
        Assert.Empty(result.PackErrors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. Runbook file missing on disk → pack fails validation Rule 13 → skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_RunbookFileMissing_PackFailsValidationAndIsSkipped()
    {
        var dir = CreatePackDirectory("missing-files");
        // Intentionally do NOT create runbooks/gone.md on disk.
        // FileSystemPackLoader Rule 13 marks the pack invalid when a
        // runbook file does not exist, so the enricher skips it.
        WritePackJson(dir, MakeManifest(
            "missing-files",
            minimumMode: "A",
            runbooks: new[] { new PackRunbook("ghost-rb", "runbooks/gone.md") }));

        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackErrors);
        Assert.Empty(result.PackRunbooks);
        Assert.Empty(result.PackEvidenceCollectors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. Multiple packs — aggregates runbooks from all
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_MultiplePacks_AggregatesAll()
    {
        var dir1 = CreatePackDirectory("pack-alpha");
        WriteFile(dir1, "runbooks/alpha.md", "Alpha runbook content.");
        WritePackJson(dir1, MakeManifest(
            "pack-alpha",
            minimumMode: "A",
            runbooks: new[] { new PackRunbook("alpha-rb", "runbooks/alpha.md") }));

        var dir2 = CreatePackDirectory("pack-beta");
        WriteFile(dir2, "runbooks/beta.md", "Beta runbook content.");
        WritePackJson(dir2, MakeManifest(
            "pack-beta",
            minimumMode: "A",
            runbooks: new[] { new PackRunbook("beta-rb", "runbooks/beta.md") }));

        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackErrors);
        Assert.Equal(2, result.PackRunbooks.Count);

        var ids = result.PackRunbooks.Select(r => r.RunbookId).OrderBy(n => n).ToList();
        Assert.Equal("alpha-rb", ids[0]);
        Assert.Equal("beta-rb", ids[1]);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. Large runbook content is truncated at 2 000 chars
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_LargeRunbookContent_IsTruncatedAt2000Chars()
    {
        var dir = CreatePackDirectory("big-rb");
        var longContent = new string('X', 3_000);
        WriteFile(dir, "runbooks/big.md", longContent);
        WritePackJson(dir, MakeManifest(
            "big-rb",
            minimumMode: "A",
            runbooks: new[] { new PackRunbook("big-runbook", "runbooks/big.md") }));

        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Single(result.PackRunbooks);
        Assert.NotNull(result.PackRunbooks[0].ContentSnippet);
        Assert.Equal(2_000, result.PackRunbooks[0].ContentSnippet!.Length);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. Mixed-mode collectors — only Mode-A returned
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_MixedModeCollectors_OnlyModeAReturned()
    {
        var dir = CreatePackDirectory("multi-ec");
        WriteFile(dir, "queries/a.kql", "HeartbeatA");
        WriteFile(dir, "queries/b.kql", "HeartbeatB");
        WritePackJson(dir, MakeManifest(
            "multi-ec",
            minimumMode: "A",
            evidenceCollectors: new[]
            {
                new EvidenceCollector("ec-a", "A", "queries/a.kql"),
                new EvidenceCollector("ec-b", "B", "queries/b.kql")
            }));

        var enricher = CreateEnricher();

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackErrors);
        Assert.Single(result.PackEvidenceCollectors);
        Assert.Equal("ec-a", result.PackEvidenceCollectors[0].EvidenceCollectorId);
        Assert.Equal("HeartbeatA", result.PackEvidenceCollectors[0].KqlContent);
    }
}
