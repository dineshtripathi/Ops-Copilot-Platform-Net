using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackTriageEnricher"/> — the Mode-A triage enrichment logic.
/// Mocks <see cref="IPackCatalog"/> and <see cref="IPackFileReader"/> (MockBehavior.Strict).
/// </summary>
public sealed class PackTriageEnricherTests
{
    // ── Helpers ────────────────────────────────────────────────

    private static LoadedPack MakePack(
        string name,
        bool isValid = true,
        string minimumMode = "A",
        IReadOnlyList<EvidenceCollector>? evidenceCollectors = null,
        IReadOnlyList<PackRunbook>? runbooks = null)
    {
        var manifest = new PackManifest(
            Name: name,
            Version: "1.0.0",
            Description: $"Test {name}",
            ResourceTypes: new[] { "Microsoft.Compute/virtualMachines" },
            MinimumMode: minimumMode,
            EvidenceCollectors: evidenceCollectors ?? Array.Empty<EvidenceCollector>(),
            Runbooks: runbooks ?? Array.Empty<PackRunbook>(),
            SafeActions: Array.Empty<PackSafeAction>());

        var validation = new PackValidationResult(isValid, isValid ? Array.Empty<string>() : new[] { "error" });
        return new LoadedPack(manifest, $"/packs/{name}", validation);
    }

    private static (PackTriageEnricher Enricher, Mock<IPackCatalog> Catalog, Mock<IPackFileReader> FileReader) CreateEnricher()
    {
        var catalog = new Mock<IPackCatalog>(MockBehavior.Strict);
        var fileReader = new Mock<IPackFileReader>(MockBehavior.Strict);
        var enricher = new PackTriageEnricher(catalog.Object, fileReader.Object, NullLogger<PackTriageEnricher>.Instance);
        return (enricher, catalog, fileReader);
    }

    // ═══════════════════════════════════════════════════════════════
    // Empty catalog
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_EmptyCatalog_ReturnsEmptyEnrichment()
    {
        var (enricher, catalog, _) = CreateEnricher();
        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<LoadedPack>());

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackRunbooks);
        Assert.Empty(result.PackEvidenceCollectors);
        Assert.Empty(result.PackErrors);
        catalog.VerifyAll();
    }

    // ═══════════════════════════════════════════════════════════════
    // Runbook discovery
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_PackWithRunbooks_ReturnsRunbookDetails()
    {
        var pack = MakePack("azure-vm",
            runbooks: new[] { new PackRunbook("rb1", "runbooks/restart.md") });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "runbooks/restart.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("# Restart VM\nStep 1...");

        var result = await enricher.EnrichAsync();

        Assert.Single(result.PackRunbooks);
        var rb = result.PackRunbooks[0];
        Assert.Equal("azure-vm", rb.PackName);
        Assert.Equal("rb1", rb.RunbookId);
        Assert.Equal("runbooks/restart.md", rb.File);
        Assert.Equal("# Restart VM\nStep 1...", rb.ContentSnippet);
        Assert.Empty(result.PackErrors);
        catalog.VerifyAll();
        fileReader.VerifyAll();
    }

    // ═══════════════════════════════════════════════════════════════
    // Evidence collector discovery — Mode A only
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_ModeACollector_ReturnsCollectorDetail()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("Perf | where CounterName == 'cpu'");

        var result = await enricher.EnrichAsync();

        Assert.Single(result.PackEvidenceCollectors);
        var ec = result.PackEvidenceCollectors[0];
        Assert.Equal("azure-vm", ec.PackName);
        Assert.Equal("ec1", ec.EvidenceCollectorId);
        Assert.Equal("A", ec.RequiredMode);
        Assert.Equal("queries/cpu.kql", ec.QueryFile);
        Assert.Equal("Perf | where CounterName == 'cpu'", ec.KqlContent);
        Assert.Empty(result.PackErrors);
    }

    [Fact]
    public async Task EnrichAsync_ModeBCollector_IsFilteredOut()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec-b", "B", "queries/deep.kql") });
        var (enricher, catalog, _) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        // fileReader should NOT be called — Mode B collector is skipped

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackEvidenceCollectors);
        Assert.Empty(result.PackErrors);
    }

    // ═══════════════════════════════════════════════════════════════
    // Invalid pack skipping
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_InvalidPack_IsSkipped()
    {
        var validPack = MakePack("good-pack",
            runbooks: new[] { new PackRunbook("rb1", "r.md") });
        var invalidPack = MakePack("bad-pack", isValid: false,
            runbooks: new[] { new PackRunbook("rb2", "r2.md") });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { validPack, invalidPack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/good-pack", "r.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("content");
        // no fileReader setup for bad-pack → Strict mock will fail if called

        var result = await enricher.EnrichAsync();

        Assert.Single(result.PackRunbooks);
        Assert.Equal("good-pack", result.PackRunbooks[0].PackName);
        Assert.Empty(result.PackErrors);
    }

    // ═══════════════════════════════════════════════════════════════
    // Catalog exception → error in result
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_CatalogThrows_ReturnsError()
    {
        var (enricher, catalog, _) = CreateEnricher();
        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("Catalog boom"));

        var result = await enricher.EnrichAsync();

        Assert.Empty(result.PackRunbooks);
        Assert.Empty(result.PackEvidenceCollectors);
        Assert.Single(result.PackErrors);
        Assert.Contains("Catalog boom", result.PackErrors[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    // File reader exception → non-fatal error, others still collected
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_FileReaderThrows_AddsErrorButContinues()
    {
        var pack = MakePack("azure-vm",
            runbooks: new[]
            {
                new PackRunbook("rb-fail", "fail.md"),
                new PackRunbook("rb-ok", "ok.md")
            });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "fail.md", It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new IOException("Disk error"));
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "ok.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("good content");

        var result = await enricher.EnrichAsync();

        Assert.Single(result.PackRunbooks);
        Assert.Equal("rb-ok", result.PackRunbooks[0].RunbookId);
        Assert.Single(result.PackErrors);
        Assert.Contains("rb-fail", result.PackErrors[0]);
        Assert.Contains("Disk error", result.PackErrors[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    // Content truncation at MaxSnippetLength (2000)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_LargeRunbookContent_IsTruncatedAt2000()
    {
        var longContent = new string('X', 3000);
        var pack = MakePack("azure-vm",
            runbooks: new[] { new PackRunbook("rb1", "long.md") });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "long.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(longContent);

        var result = await enricher.EnrichAsync();

        Assert.Single(result.PackRunbooks);
        Assert.Equal(2000, result.PackRunbooks[0].ContentSnippet!.Length);
    }

    [Fact]
    public async Task EnrichAsync_ShortRunbookContent_IsNotTruncated()
    {
        const string shortContent = "Short runbook";
        var pack = MakePack("azure-vm",
            runbooks: new[] { new PackRunbook("rb1", "short.md") });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "short.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(shortContent);

        var result = await enricher.EnrichAsync();

        Assert.Equal(shortContent, result.PackRunbooks[0].ContentSnippet);
    }

    // ═══════════════════════════════════════════════════════════════
    // Multiple packs combined
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_MultiplePacks_AggregatesAll()
    {
        var pack1 = MakePack("vm-pack",
            runbooks: new[] { new PackRunbook("rb1", "r1.md") },
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "q1.kql") });
        var pack2 = MakePack("k8s-pack",
            runbooks: new[] { new PackRunbook("rb2", "r2.md") });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack1, pack2 });
        fileReader.Setup(f => f.ReadFileAsync("/packs/vm-pack", "r1.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("vm runbook");
        fileReader.Setup(f => f.ReadFileAsync("/packs/vm-pack", "q1.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("Perf query");
        fileReader.Setup(f => f.ReadFileAsync("/packs/k8s-pack", "r2.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("k8s runbook");

        var result = await enricher.EnrichAsync();

        Assert.Equal(2, result.PackRunbooks.Count);
        Assert.Single(result.PackEvidenceCollectors);
        Assert.Empty(result.PackErrors);
        Assert.Contains(result.PackRunbooks, r => r.PackName == "vm-pack");
        Assert.Contains(result.PackRunbooks, r => r.PackName == "k8s-pack");
        Assert.Equal("vm-pack", result.PackEvidenceCollectors[0].PackName);
    }

    // ═══════════════════════════════════════════════════════════════
    // Null file content (file not found) → included with null snippet
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_RunbookFileNotFound_SnippetIsNull()
    {
        var pack = MakePack("azure-vm",
            runbooks: new[] { new PackRunbook("rb1", "missing.md") });
        var (enricher, catalog, fileReader) = CreateEnricher();

        catalog.Setup(c => c.FindByMinimumModeAsync("A", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "missing.md", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((string?)null);

        var result = await enricher.EnrichAsync();

        Assert.Single(result.PackRunbooks);
        Assert.Null(result.PackRunbooks[0].ContentSnippet);
        Assert.Empty(result.PackErrors);
    }
}
