using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackSafeActionProposer"/> — the Mode-B+ safe-action
/// proposal logic.  Mocks <see cref="IPackCatalog"/> and <see cref="IPackFileReader"/>
/// (MockBehavior.Strict), <see cref="IPacksTelemetry"/> (MockBehavior.Loose).
/// </summary>
public sealed class PackSafeActionProposerTests
{
    private const string TestTenantId = "tenant-unit-test";

    // ── Helpers ────────────────────────────────────────────────

    private static LoadedPack MakePack(
        string name,
        bool isValid = true,
        string minimumMode = "A",
        IReadOnlyList<PackSafeAction>? safeActions = null)
    {
        var manifest = new PackManifest(
            Name: name,
            Version: "1.0.0",
            Description: $"Test {name}",
            ResourceTypes: new[] { "Microsoft.Compute/virtualMachines" },
            MinimumMode: minimumMode,
            EvidenceCollectors: Array.Empty<EvidenceCollector>(),
            Runbooks: Array.Empty<PackRunbook>(),
            SafeActions: safeActions ?? Array.Empty<PackSafeAction>());

        var validation = new PackValidationResult(isValid, isValid ? Array.Empty<string>() : new[] { "error" });
        return new LoadedPack(manifest, $"/packs/{name}", validation);
    }

    private static IConfiguration BuildConfig(
        string deploymentMode = "B",
        string safeActionsEnabled = "true")
    {
        var data = new Dictionary<string, string?>
        {
            ["Packs:DeploymentMode"]      = deploymentMode,
            ["Packs:SafeActionsEnabled"]   = safeActionsEnabled
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    private static (
        PackSafeActionProposer  Proposer,
        Mock<IPackCatalog>      Catalog,
        Mock<IPackFileReader>   FileReader,
        Mock<IPacksTelemetry>   Telemetry)
        CreateProposer(IConfiguration? config = null)
    {
        var catalog    = new Mock<IPackCatalog>(MockBehavior.Strict);
        var fileReader = new Mock<IPackFileReader>(MockBehavior.Strict);
        var telemetry  = new Mock<IPacksTelemetry>(MockBehavior.Loose);

        var cfg = config ?? BuildConfig();

        var proposer = new PackSafeActionProposer(
            catalog.Object,
            fileReader.Object,
            cfg,
            NullLogger<PackSafeActionProposer>.Instance,
            telemetry.Object);

        return (proposer, catalog, fileReader, telemetry);
    }

    // ── Request factory ───────────────────────────────────────

    private static PackSafeActionProposalRequest MakeRequest(
        string deploymentMode = "B",
        string? tenantId = TestTenantId) =>
        new(deploymentMode, tenantId);

    // ═══════════════════════════════════════════════════════════════
    // 1. Mode A → proposal skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_ModeA_SkipsProposal()
    {
        var config = BuildConfig(deploymentMode: "A");
        var (proposer, _, _, _) = CreateProposer(config);

        var result = await proposer.ProposeAsync(MakeRequest("A"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Feature disabled → proposal skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_FeatureDisabled_SkipsProposal()
    {
        var config = BuildConfig(safeActionsEnabled: "false");
        var (proposer, _, _, _) = CreateProposer(config);

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Mode B + enabled → happy path with definition file
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_ModeBEnabled_ProposesActions()
    {
        var action = new PackSafeAction("restart-vm", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"displayName":"Restart VM","actionType":"restart","parameters":{"vmName":"test-vm"}}""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("azure-vm", item.PackName);
        Assert.Equal("restart-vm", item.ActionId);
        Assert.Equal("Restart VM", item.DisplayName);
        Assert.Equal("restart", item.ActionType);
        Assert.Equal("B", item.RequiresMode);
        Assert.Equal("actions/restart.json", item.DefinitionFile);
        Assert.Equal("""{"vmName":"test-vm"}""", item.ParametersJson);
        Assert.Null(item.ErrorMessage);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Catalog throws → error in result
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_CatalogThrows_ReturnsError()
    {
        var config = BuildConfig();
        var (proposer, catalog, _, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("Catalog boom"));

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Empty(result.Proposals);
        Assert.Single(result.Errors);
        Assert.Contains("Catalog boom", result.Errors[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. No eligible packs (pack mode above deployment) → empty
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_NoEligiblePacks_ReturnsEmpty()
    {
        var action = new PackSafeAction("sa1", "C", "actions/sa1.json");
        var pack = MakePack("future-pack", minimumMode: "C", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");
        var (proposer, catalog, _, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. Invalid packs are skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_InvalidPack_IsSkipped()
    {
        var action = new PackSafeAction("sa1", "A", "actions/sa1.json");
        var pack = MakePack("bad-pack", isValid: false, safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, _, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. No eligible actions (action mode above deployment) → empty
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_NoEligibleActions_ReturnsEmpty()
    {
        var action = new PackSafeAction("sa-c", "C", "actions/c.json");
        var pack = MakePack("azure-vm", minimumMode: "A", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");
        var (proposer, catalog, _, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. Definition file read throws → error item with ErrorMessage
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_DefinitionFileReadThrows_RecordsError()
    {
        var action = new PackSafeAction("restart-vm", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("File not found"));

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("azure-vm", item.PackName);
        Assert.Equal("restart-vm", item.ActionId);
        Assert.Equal("restart-vm", item.DisplayName);   // Falls back to action ID
        Assert.Equal("unknown", item.ActionType);
        Assert.Equal("File not found", item.ErrorMessage);
        Assert.Single(result.Errors);
        Assert.Contains("File not found", result.Errors[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. Invalid JSON in definition → error item
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_InvalidJson_RecordsError()
    {
        var action = new PackSafeAction("restart-vm", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("NOT-VALID-JSON{{{");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        // Invalid JSON → caught by try/catch → error item
        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("unknown", item.ActionType);
        Assert.NotNull(item.ErrorMessage);
        Assert.Single(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. Multiple packs, multiple actions → aggregated proposals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_MultiplePacksMultipleActions_AggregatesProposals()
    {
        var actions1 = new[]
        {
            new PackSafeAction("restart-vm", "A", "actions/restart.json"),
            new PackSafeAction("scale-up",   "B", "actions/scale.json")
        };
        var actions2 = new[]
        {
            new PackSafeAction("rotate-key", "A", "actions/rotate.json")
        };
        var pack1 = MakePack("azure-vm", safeActions: actions1);
        var pack2 = MakePack("azure-kv", safeActions: actions2);
        var config = BuildConfig(deploymentMode: "B");
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack1, pack2 });

        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"displayName":"Restart VM","actionType":"restart","parameters":{}}""");
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "actions/scale.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"displayName":"Scale Up","actionType":"scale","parameters":{"sku":"Standard_D4"}}""");
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-kv", "actions/rotate.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"displayName":"Rotate Key","actionType":"key-rotation","parameters":{"keyName":"primary"}}""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Equal(3, result.Proposals.Count);
        Assert.Empty(result.Errors);

        Assert.Equal("Restart VM", result.Proposals[0].DisplayName);
        Assert.Equal("azure-vm", result.Proposals[0].PackName);

        Assert.Equal("Scale Up", result.Proposals[1].DisplayName);
        Assert.Equal("azure-vm", result.Proposals[1].PackName);

        Assert.Equal("Rotate Key", result.Proposals[2].DisplayName);
        Assert.Equal("azure-kv", result.Proposals[2].PackName);
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. Mode C includes A, B, C actions
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_ModeCIncludesABCActions()
    {
        var actions = new[]
        {
            new PackSafeAction("sa-a", "A", "actions/a.json"),
            new PackSafeAction("sa-b", "B", "actions/b.json"),
            new PackSafeAction("sa-c", "C", "actions/c.json"),
        };
        var pack = MakePack("azure-vm", safeActions: actions);
        var config = BuildConfig(deploymentMode: "C", safeActionsEnabled: "true");
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"displayName":"Action","actionType":"generic","parameters":{}}""");

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Equal(3, result.Proposals.Count);
        Assert.Equal("sa-a", result.Proposals[0].ActionId);
        Assert.Equal("sa-b", result.Proposals[1].ActionId);
        Assert.Equal("sa-c", result.Proposals[2].ActionId);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. Null definition file → item without parameters
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_NullDefinitionFile_ReturnsItemWithoutParameters()
    {
        var action = new PackSafeAction("quick-check", "B", null);
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, _, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("quick-check", item.ActionId);
        Assert.Equal("quick-check", item.DisplayName);  // Falls back to action ID
        Assert.Equal("unknown", item.ActionType);
        Assert.Null(item.ParametersJson);
        Assert.Null(item.DefinitionFile);
        Assert.Null(item.ErrorMessage);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. Telemetry recorded on success
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_TelemetryRecordedOnSuccess()
    {
        var action = new PackSafeAction("restart-vm", "B", null);
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, _, telemetry) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        await proposer.ProposeAsync(MakeRequest("B"));

        telemetry.Verify(
            t => t.RecordEvidenceAttempt("B", TestTenantId, It.IsAny<string>()),
            Times.Once);
        telemetry.Verify(
            t => t.RecordCollectorSuccess("azure-vm", "restart-vm", TestTenantId, It.IsAny<string>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. Telemetry recorded on failure
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_TelemetryRecordedOnFailure()
    {
        var action = new PackSafeAction("restart-vm", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, fileReader, telemetry) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk read error"));

        await proposer.ProposeAsync(MakeRequest("B"));

        telemetry.Verify(
            t => t.RecordEvidenceAttempt("B", TestTenantId, It.IsAny<string>()),
            Times.Once);
        telemetry.Verify(
            t => t.RecordCollectorFailure("azure-vm", "restart-vm", TestTenantId, "DefinitionReadError", It.IsAny<string>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // 15. Skipped telemetry for Mode A
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_ModeA_RecordsSkippedTelemetry()
    {
        var config = BuildConfig(deploymentMode: "A");
        var (proposer, _, _, telemetry) = CreateProposer(config);

        await proposer.ProposeAsync(MakeRequest("A"));

        telemetry.Verify(
            t => t.RecordEvidenceSkipped("A", TestTenantId),
            Times.Once);
        telemetry.Verify(
            t => t.RecordEvidenceAttempt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════
    // 16. Static helper — IsModeEligible
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("A", false)]
    [InlineData("a", false)]
    [InlineData("B", true)]
    [InlineData("b", true)]
    [InlineData("C", true)]
    [InlineData("c", true)]
    [InlineData("",  false)]
    public void IsModeEligible_ReturnsExpected(string mode, bool expected)
    {
        Assert.Equal(expected, PackSafeActionProposer.IsModeEligible(mode));
    }

    // ═══════════════════════════════════════════════════════════════
    // 17. Static helper — IsModeAtOrBelow
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("A", "B", true)]
    [InlineData("B", "B", true)]
    [InlineData("C", "B", false)]
    [InlineData("A", "C", true)]
    [InlineData("B", "C", true)]
    [InlineData("C", "C", true)]
    [InlineData("a", "b", true)]
    [InlineData("c", "b", false)]
    public void IsModeAtOrBelow_ReturnsExpected(string packMode, string deploymentMode, bool expected)
    {
        Assert.Equal(expected, PackSafeActionProposer.IsModeAtOrBelow(packMode, deploymentMode));
    }

    // ═══════════════════════════════════════════════════════════════
    // 18. FileReader returns null content → fallback values
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_FileReaderReturnsNull_FallbackValues()
    {
        var action = new PackSafeAction("restart-vm", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig();
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("restart-vm", item.DisplayName);  // Fallback to action ID
        Assert.Equal("unknown", item.ActionType);       // Fallback
        Assert.Null(item.ParametersJson);
        Assert.Null(item.ErrorMessage);
        Assert.Empty(result.Errors);
    }
}
