using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
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
        CreateProposer(
            IConfiguration? config = null,
            IToolAllowlistPolicy? toolPolicy = null)
    {
        var catalog    = new Mock<IPackCatalog>(MockBehavior.Strict);
        var fileReader = new Mock<IPackFileReader>(MockBehavior.Strict);
        var telemetry  = new Mock<IPacksTelemetry>(MockBehavior.Loose);

        var cfg = config ?? BuildConfig();
        var scopeFactory = CreateScopeFactory(toolPolicy);

        var proposer = new PackSafeActionProposer(
            catalog.Object,
            fileReader.Object,
            cfg,
            NullLogger<PackSafeActionProposer>.Instance,
            telemetry.Object,
            scopeFactory.Object);

        return (proposer, catalog, fileReader, telemetry);
    }

    private static Mock<IServiceScopeFactory> CreateScopeFactory(
        IToolAllowlistPolicy? toolPolicy = null)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IToolAllowlistPolicy)))
            .Returns(toolPolicy!);

        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);

        return scopeFactory;
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
        Assert.True(item.IsExecutableNow);
        Assert.Null(item.ExecutionBlockedReason);
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
    // 7. Action above deployment mode → included as non-executable
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_ActionAboveDeploymentMode_ReturnsNotExecutableProposal()
    {
        var action = new PackSafeAction("sa-c", "C", "actions/c.json");
        var pack = MakePack("azure-vm", minimumMode: "A", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/c.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Escalate Action","actionType":"escalation","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("azure-vm", item.PackName);
        Assert.Equal("sa-c", item.ActionId);
        Assert.Equal("Escalate Action", item.DisplayName);
        Assert.Equal("escalation", item.ActionType);
        Assert.Equal("C", item.RequiresMode);
        Assert.False(item.IsExecutableNow);
        Assert.Equal("requires_higher_mode", item.ExecutionBlockedReason);
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
        Assert.True(item.IsExecutableNow);
        Assert.Null(item.ExecutionBlockedReason);
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
        Assert.True(item.IsExecutableNow);
        Assert.Null(item.ExecutionBlockedReason);
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
        Assert.True(result.Proposals[0].IsExecutableNow);
        Assert.Null(result.Proposals[0].ExecutionBlockedReason);

        Assert.Equal("Scale Up", result.Proposals[1].DisplayName);
        Assert.Equal("azure-vm", result.Proposals[1].PackName);
        Assert.True(result.Proposals[1].IsExecutableNow);
        Assert.Null(result.Proposals[1].ExecutionBlockedReason);

        Assert.Equal("Rotate Key", result.Proposals[2].DisplayName);
        Assert.Equal("azure-kv", result.Proposals[2].PackName);
        Assert.True(result.Proposals[2].IsExecutableNow);
        Assert.Null(result.Proposals[2].ExecutionBlockedReason);
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
        Assert.True(result.Proposals[0].IsExecutableNow);
        Assert.Null(result.Proposals[0].ExecutionBlockedReason);
        Assert.Equal("sa-b", result.Proposals[1].ActionId);
        Assert.True(result.Proposals[1].IsExecutableNow);
        Assert.Null(result.Proposals[1].ExecutionBlockedReason);
        Assert.Equal("sa-c", result.Proposals[2].ActionId);
        Assert.True(result.Proposals[2].IsExecutableNow);
        Assert.Null(result.Proposals[2].ExecutionBlockedReason);
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
        Assert.True(item.IsExecutableNow);
        Assert.Null(item.ExecutionBlockedReason);
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
        Assert.True(item.IsExecutableNow);
        Assert.Null(item.ExecutionBlockedReason);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 19. Mode B with mixed eligible/ineligible actions
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_ModeBMixedActions_SetsCorrectEligibility()
    {
        var actions = new[]
        {
            new PackSafeAction("sa-a", "A", "actions/a.json"),
            new PackSafeAction("sa-b", "B", "actions/b.json"),
            new PackSafeAction("sa-c", "C", "actions/c.json"),
        };
        var pack = MakePack("azure-vm", safeActions: actions);
        var config = BuildConfig(deploymentMode: "B");
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Action","actionType":"generic","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Equal(3, result.Proposals.Count);
        Assert.Empty(result.Errors);

        // A and B actions are executable in Mode B
        var saA = result.Proposals.Single(p => p.ActionId == "sa-a");
        Assert.True(saA.IsExecutableNow);
        Assert.Null(saA.ExecutionBlockedReason);

        var saB = result.Proposals.Single(p => p.ActionId == "sa-b");
        Assert.True(saB.IsExecutableNow);
        Assert.Null(saB.ExecutionBlockedReason);

        // C action is NOT executable in Mode B — blocked
        var saC = result.Proposals.Single(p => p.ActionId == "sa-c");
        Assert.False(saC.IsExecutableNow);
        Assert.Equal("requires_higher_mode", saC.ExecutionBlockedReason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 20. Non-executable action with file-read error — both recorded
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeAsync_NonExecutableActionFileReadError_RecordsErrorWithBlockedReason()
    {
        var action = new PackSafeAction("sa-c", "C", "actions/c.json");
        var pack = MakePack("azure-vm", minimumMode: "A", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");
        var (proposer, catalog, fileReader, _) = CreateProposer(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/c.json", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("Not found"));

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("sa-c", item.ActionId);
        Assert.Equal("Not found", item.ErrorMessage);
        Assert.False(item.IsExecutableNow);
        Assert.Equal("requires_higher_mode", item.ExecutionBlockedReason);
        Assert.Single(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Governance Preview Tests (Slice 46) ───────────────────────
    // ═══════════════════════════════════════════════════════════════

    // 21. Policy allows → GovernanceAllowed = true

    [Fact]
    public async Task ProposeAsync_GovernanceAllowed_WhenPolicyAllows()
    {
        var action = new PackSafeAction("sa-restart", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool(TestTenantId, "restart_vm"))
              .Returns(PolicyDecision.Allow());

        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Restart VM","actionType":"restart_vm","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        var item = Assert.Single(result.Proposals);
        Assert.True(item.GovernanceAllowed);
        Assert.Equal("ALLOWED", item.GovernanceReasonCode);
        Assert.NotNull(item.GovernanceMessage);
        policy.VerifyAll();
    }

    // 22. Policy denies → GovernanceAllowed = false with reason

    [Fact]
    public async Task ProposeAsync_GovernanceDenied_WhenPolicyDenies()
    {
        var action = new PackSafeAction("sa-delete", "B", "actions/delete.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool(TestTenantId, "delete_vm"))
              .Returns(PolicyDecision.Deny("not_allowlisted", "Tool delete_vm is not on the allowlist."));

        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/delete.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Delete VM","actionType":"delete_vm","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        var item = Assert.Single(result.Proposals);
        Assert.False(item.GovernanceAllowed);
        Assert.Equal("not_allowlisted", item.GovernanceReasonCode);
        Assert.Equal("Tool delete_vm is not on the allowlist.", item.GovernanceMessage);
        policy.VerifyAll();
    }

    // 23. Null tenantId → governance fields stay null

    [Fact]
    public async Task ProposeAsync_GovernanceNull_WhenTenantIdMissing()
    {
        var action = new PackSafeAction("sa-restart", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        // Policy should never be called when tenantId is null
        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Restart VM","actionType":"restart_vm","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B", tenantId: null));

        var item = Assert.Single(result.Proposals);
        Assert.Null(item.GovernanceAllowed);
        Assert.Null(item.GovernanceReasonCode);
        Assert.Null(item.GovernanceMessage);
    }

    // 24. tenantId = "unknown" → governance fields stay null

    [Fact]
    public async Task ProposeAsync_GovernanceNull_WhenTenantIdUnknown()
    {
        var action = new PackSafeAction("sa-restart", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        // Policy should never be called when tenantId is "unknown"
        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Restart VM","actionType":"restart_vm","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B", tenantId: "unknown"));

        var item = Assert.Single(result.Proposals);
        Assert.Null(item.GovernanceAllowed);
        Assert.Null(item.GovernanceReasonCode);
        Assert.Null(item.GovernanceMessage);
    }

    // 25. No policy registered → governance fields stay null

    [Fact]
    public async Task ProposeAsync_GovernanceNull_WhenNoPolicyRegistered()
    {
        var action = new PackSafeAction("sa-restart", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        // toolPolicy = null → CreateScopeFactory returns provider that resolves null
        var (proposer, catalog, fileReader, _) = CreateProposer(config, toolPolicy: null);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Restart VM","actionType":"restart_vm","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        var item = Assert.Single(result.Proposals);
        Assert.Null(item.GovernanceAllowed);
        Assert.Null(item.GovernanceReasonCode);
        Assert.Null(item.GovernanceMessage);
    }

    // 26. Policy throws → safe fallback

    [Fact]
    public async Task ProposeAsync_GovernanceFailed_WhenPolicyThrows()
    {
        var action = new PackSafeAction("sa-restart", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool(TestTenantId, "restart_vm"))
              .Throws(new InvalidOperationException("Config error"));

        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Restart VM","actionType":"restart_vm","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        var item = Assert.Single(result.Proposals);
        Assert.False(item.GovernanceAllowed);
        Assert.Equal("governance_preview_failed", item.GovernanceReasonCode);
        Assert.Equal("Governance preview could not be computed.", item.GovernanceMessage);
    }

    // 27. Multiple actions with mixed governance results

    [Fact]
    public async Task ProposeAsync_GovernancePerAction_MixedResults()
    {
        var actions = new[]
        {
            new PackSafeAction("sa-restart", "B", "actions/restart.json"),
            new PackSafeAction("sa-delete", "B", "actions/delete.json"),
        };
        var pack = MakePack("azure-vm", safeActions: actions);
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool(TestTenantId, "restart_vm"))
              .Returns(PolicyDecision.Allow());
        policy.Setup(p => p.CanUseTool(TestTenantId, "delete_vm"))
              .Returns(PolicyDecision.Deny("not_allowlisted", "Tool delete_vm is not on the allowlist."));

        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Restart VM","actionType":"restart_vm","parameters":{} }""");
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/delete.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Delete VM","actionType":"delete_vm","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Equal(2, result.Proposals.Count);

        var restart = result.Proposals.Single(p => p.ActionId == "sa-restart");
        Assert.True(restart.GovernanceAllowed);
        Assert.Equal("ALLOWED", restart.GovernanceReasonCode);

        var delete = result.Proposals.Single(p => p.ActionId == "sa-delete");
        Assert.False(delete.GovernanceAllowed);
        Assert.Equal("not_allowlisted", delete.GovernanceReasonCode);
        policy.VerifyAll();
    }

    // 28. Error path also gets governance enrichment

    [Fact]
    public async Task ProposeAsync_GovernanceOnErrorPath_WhenDefinitionReadFails()
    {
        var action = new PackSafeAction("sa-restart", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        // Error path sets ActionType = "unknown"
        policy.Setup(p => p.CanUseTool(TestTenantId, "unknown"))
              .Returns(PolicyDecision.Deny("not_allowlisted", "Tool unknown is not on the allowlist."));

        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/restart.json", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("Not found"));

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        var item = Assert.Single(result.Proposals);
        Assert.NotNull(item.ErrorMessage);
        Assert.False(item.GovernanceAllowed);
        Assert.Equal("not_allowlisted", item.GovernanceReasonCode);
    }

    // 29. Mode A → no governance computed (no proposals at all)

    [Fact]
    public async Task ProposeAsync_ModeA_NoGovernanceComputed()
    {
        var action = new PackSafeAction("sa-restart", "B", "actions/restart.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "A");

        // Policy should never be called in Mode A (proposals skipped entirely)
        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        var (proposer, catalog, _, _) = CreateProposer(config, policy.Object);

        var result = await proposer.ProposeAsync(MakeRequest("A"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // 30. Governance uses actionType from definition JSON

    [Fact]
    public async Task ProposeAsync_GovernanceUsesActionType_FromDefinition()
    {
        var action = new PackSafeAction("sa-x", "B", "actions/x.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        // The actionType in the JSON is "scale_up", not the actionId "sa-x"
        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool(TestTenantId, "scale_up"))
              .Returns(PolicyDecision.Allow());

        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/x.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Scale Up","actionType":"scale_up","parameters":{"size":"large"} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        var item = Assert.Single(result.Proposals);
        Assert.Equal("scale_up", item.ActionType);
        Assert.True(item.GovernanceAllowed);
        policy.Verify(p => p.CanUseTool(TestTenantId, "scale_up"), Times.Once);
    }

    // 31. ReasonCode and Message propagated from PolicyDecision

    [Fact]
    public async Task ProposeAsync_GovernanceReasonCodeAndMessage_PropagatedFromPolicy()
    {
        var action = new PackSafeAction("sa-drain", "B", "actions/drain.json");
        var pack = MakePack("azure-vm", safeActions: new[] { action });
        var config = BuildConfig(deploymentMode: "B");

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool(TestTenantId, "drain_node"))
              .Returns(PolicyDecision.Deny("tenant_restricted", "Tenant does not permit drain_node."));

        var (proposer, catalog, fileReader, _) = CreateProposer(config, policy.Object);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "actions/drain.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "displayName":"Drain Node","actionType":"drain_node","parameters":{} }""");

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        var item = Assert.Single(result.Proposals);
        Assert.False(item.GovernanceAllowed);
        Assert.Equal("tenant_restricted", item.GovernanceReasonCode);
        Assert.Equal("Tenant does not permit drain_node.", item.GovernanceMessage);
        policy.VerifyAll();
    }
}
