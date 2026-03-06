using Moq;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackCatalog"/> query methods added in Slice 35.
/// Uses a mock <see cref="IPackLoader"/> — no TestHost required.
/// </summary>
public class PackCatalogQueryTests
{
    // ── Helpers ────────────────────────────────────────────────

    private static LoadedPack MakePack(
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
            Description: $"Test {name}",
            ResourceTypes: resourceTypes ?? new[] { "Microsoft.Compute/virtualMachines" },
            MinimumMode: minimumMode,
            EvidenceCollectors: evidenceCollectors ?? Array.Empty<EvidenceCollector>(),
            Runbooks: runbooks ?? Array.Empty<PackRunbook>(),
            SafeActions: safeActions ?? Array.Empty<PackSafeAction>());

        var validation = new PackValidationResult(isValid, isValid ? Array.Empty<string>() : new[] { "error" });
        return new LoadedPack(manifest, $"/packs/{name}", validation);
    }

    private static (PackCatalog Catalog, Mock<IPackLoader> Loader) CreateCatalog(
        params LoadedPack[] packs)
    {
        var loader = new Mock<IPackLoader>(MockBehavior.Strict);
        loader.Setup(l => l.LoadAllAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(packs.ToList().AsReadOnly());

        var catalog = new PackCatalog(loader.Object);
        return (catalog, loader);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetByNameAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetByNameAsync_ExistingPack_ReturnsPack()
    {
        var pack = MakePack("azure-vm");
        var (catalog, _) = CreateCatalog(pack);

        var result = await catalog.GetByNameAsync("azure-vm");

        Assert.NotNull(result);
        Assert.Equal("azure-vm", result!.Manifest.Name);
    }

    [Fact]
    public async Task GetByNameAsync_CaseInsensitive_ReturnsPack()
    {
        var pack = MakePack("azure-vm");
        var (catalog, _) = CreateCatalog(pack);

        var result = await catalog.GetByNameAsync("AZURE-VM");

        Assert.NotNull(result);
        Assert.Equal("azure-vm", result!.Manifest.Name);
    }

    [Fact]
    public async Task GetByNameAsync_NotFound_ReturnsNull()
    {
        var (catalog, _) = CreateCatalog(MakePack("azure-vm"));

        var result = await catalog.GetByNameAsync("nonexistent");

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // FindByResourceTypeAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FindByResourceTypeAsync_Matching_ReturnsPacks()
    {
        var pack = MakePack("azure-vm", resourceTypes: new[] { "Microsoft.Compute/virtualMachines" });
        var (catalog, _) = CreateCatalog(pack);

        var results = await catalog.FindByResourceTypeAsync("Microsoft.Compute/virtualMachines");

        Assert.Single(results);
        Assert.Equal("azure-vm", results[0].Manifest.Name);
    }

    [Fact]
    public async Task FindByResourceTypeAsync_CaseInsensitive_ReturnsPacks()
    {
        var pack = MakePack("azure-vm", resourceTypes: new[] { "Microsoft.Compute/virtualMachines" });
        var (catalog, _) = CreateCatalog(pack);

        var results = await catalog.FindByResourceTypeAsync("microsoft.compute/virtualmachines");

        Assert.Single(results);
    }

    [Fact]
    public async Task FindByResourceTypeAsync_NoMatch_ReturnsEmpty()
    {
        var (catalog, _) = CreateCatalog(MakePack("azure-vm"));

        var results = await catalog.FindByResourceTypeAsync("Microsoft.Network/loadBalancers");

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════
    // FindByMinimumModeAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FindByMinimumModeAsync_Matching_ReturnsPacks()
    {
        var packA = MakePack("pack-a", minimumMode: "A");
        var packB = MakePack("pack-b", minimumMode: "B");
        var (catalog, _) = CreateCatalog(packA, packB);

        var results = await catalog.FindByMinimumModeAsync("A");

        Assert.Single(results);
        Assert.Equal("pack-a", results[0].Manifest.Name);
    }

    [Fact]
    public async Task FindByMinimumModeAsync_NoMatch_ReturnsEmpty()
    {
        var (catalog, _) = CreateCatalog(MakePack("pack-a", minimumMode: "A"));

        var results = await catalog.FindByMinimumModeAsync("Z");

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetDetailsAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDetailsAsync_ExistingPack_ProjectsCorrectly()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "q.kql") },
            runbooks: new[] { new PackRunbook("rb1", "runbooks/r.md") },
            safeActions: new[] { new PackSafeAction("sa1", "C", "def.json") });
        var (catalog, _) = CreateCatalog(pack);

        var details = await catalog.GetDetailsAsync("azure-vm");

        Assert.NotNull(details);
        Assert.Equal("azure-vm", details!.Name);
        Assert.Equal("1.0.0", details.Version);
        Assert.Equal("A", details.MinimumMode);
        Assert.Equal(1, details.EvidenceCollectorCount);
        Assert.Equal(1, details.RunbookCount);
        Assert.Equal(1, details.SafeActionCount);
        Assert.True(details.IsValid);
        Assert.Empty(details.Errors);
        Assert.Equal("/packs/azure-vm", details.PackPath);
    }

    [Fact]
    public async Task GetDetailsAsync_NotFound_ReturnsNull()
    {
        var (catalog, _) = CreateCatalog(MakePack("azure-vm"));

        var details = await catalog.GetDetailsAsync("nonexistent");

        Assert.Null(details);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetRunbooksAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRunbooksAsync_ExistingPack_ReturnsSummaries()
    {
        var pack = MakePack("azure-vm",
            runbooks: new[] { new PackRunbook("rb1", "runbooks/r.md") });
        var (catalog, _) = CreateCatalog(pack);

        var result = await catalog.GetRunbooksAsync("azure-vm");

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("rb1", result[0].Id);
        Assert.Equal("runbooks/r.md", result[0].File);
    }

    [Fact]
    public async Task GetRunbooksAsync_NotFound_ReturnsNull()
    {
        var (catalog, _) = CreateCatalog(MakePack("azure-vm"));

        Assert.Null(await catalog.GetRunbooksAsync("nonexistent"));
    }

    // ═══════════════════════════════════════════════════════════════
    // GetEvidenceCollectorsAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetEvidenceCollectorsAsync_ExistingPack_ReturnsSummaries()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "q.kql") });
        var (catalog, _) = CreateCatalog(pack);

        var result = await catalog.GetEvidenceCollectorsAsync("azure-vm");

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("ec1", result[0].Id);
        Assert.Equal("A", result[0].RequiredMode);
        Assert.Equal("q.kql", result[0].QueryFile);
    }

    [Fact]
    public async Task GetEvidenceCollectorsAsync_NotFound_ReturnsNull()
    {
        var (catalog, _) = CreateCatalog(MakePack("azure-vm"));

        Assert.Null(await catalog.GetEvidenceCollectorsAsync("ghost"));
    }

    // ═══════════════════════════════════════════════════════════════
    // GetSafeActionsAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSafeActionsAsync_ExistingPack_ReturnsSummaries()
    {
        var pack = MakePack("azure-vm",
            safeActions: new[] { new PackSafeAction("sa1", "C", "def.json") });
        var (catalog, _) = CreateCatalog(pack);

        var result = await catalog.GetSafeActionsAsync("azure-vm");

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("sa1", result[0].Id);
        Assert.Equal("C", result[0].RequiresMode);
        Assert.Equal("def.json", result[0].DefinitionFile);
    }

    [Fact]
    public async Task GetSafeActionsAsync_NotFound_ReturnsNull()
    {
        var (catalog, _) = CreateCatalog(MakePack("azure-vm"));

        Assert.Null(await catalog.GetSafeActionsAsync("nope"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Multiple packs — index correctness
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FindByResourceType_MultiplePacks_ReturnsAllMatching()
    {
        var vm = MakePack("azure-vm",
            resourceTypes: new[] { "Microsoft.Compute/virtualMachines" });
        var k8s = MakePack("k8s-basic",
            resourceTypes: new[] { "Microsoft.ContainerService/managedClusters" });
        var vmExtra = MakePack("azure-vm-extra",
            resourceTypes: new[] { "Microsoft.Compute/virtualMachines", "Microsoft.Compute/disks" });
        var (catalog, _) = CreateCatalog(vm, k8s, vmExtra);

        var results = await catalog.FindByResourceTypeAsync("Microsoft.Compute/virtualMachines");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, p => p.Manifest.Name == "azure-vm");
        Assert.Contains(results, p => p.Manifest.Name == "azure-vm-extra");
    }

    [Fact]
    public async Task LoaderCalledOnlyOnce_AcrossMultipleQueries()
    {
        var pack = MakePack("azure-vm");
        var (catalog, loader) = CreateCatalog(pack);

        // Call multiple methods — loader should only be invoked once
        _ = await catalog.GetAllAsync();
        _ = await catalog.GetByNameAsync("azure-vm");
        _ = await catalog.FindByResourceTypeAsync("Microsoft.Compute/virtualMachines");
        _ = await catalog.GetDetailsAsync("azure-vm");

        loader.Verify(l => l.LoadAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
