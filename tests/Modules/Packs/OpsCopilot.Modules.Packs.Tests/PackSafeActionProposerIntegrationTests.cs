using System.Text.Json;
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
/// Integration tests wiring the full Slice 44 safe-action proposal pipeline with real filesystem:
/// FileSystemPackLoader → PackCatalog → PackFileReader → PackSafeActionProposer.
/// Telemetry uses a loose mock — all other components are real.
/// </summary>
public sealed class PackSafeActionProposerIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public PackSafeActionProposerIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "packs-sa-integ-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private PackSafeActionProposer CreateProposer(
        string deploymentMode = "B",
        bool safeActionsEnabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Packs:RootPath"] = _tempRoot,
                ["Packs:SafeActionsEnabled"] = safeActionsEnabled ? "true" : "false"
            })
            .Build();

        var loader = new FileSystemPackLoader(config, NullLogger<FileSystemPackLoader>.Instance);
        var catalog = new PackCatalog(loader);
        var fileReader = new PackFileReader(NullLogger<PackFileReader>.Instance);
        var telemetry = new Mock<IPacksTelemetry>(MockBehavior.Loose);

        return new PackSafeActionProposer(
            catalog,
            fileReader,
            config,
            NullLogger<PackSafeActionProposer>.Instance,
            telemetry.Object);
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

    private static string MakeActionDefinition(
        string displayName = "Restart Service",
        string actionType = "AzureResourceAction",
        object? parameters = null)
    {
        var obj = new Dictionary<string, object?>
        {
            ["displayName"] = displayName,
            ["actionType"] = actionType
        };

        if (parameters is not null)
            obj["parameters"] = parameters;

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    private static PackSafeActionProposalRequest MakeRequest(string deploymentMode = "B") =>
        new(deploymentMode, "tenant-integ", "corr-integ-001");

    // ═══════════════════════════════════════════════════════════════
    // 1. Happy path — Mode C pack with definition file
    //    Rule 9 requires all safeActions.requiresMode == "C".
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ModeCPackWithDefinition_ReturnsParsedProposal()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition(
            "Restart VM",
            "AzureResourceAction",
            new { resourceId = "{resourceId}", action = "restart" }));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Empty(result.Errors);
        Assert.Single(result.Proposals);

        var p = result.Proposals[0];
        Assert.Equal("azure-vm", p.PackName);
        Assert.Equal("restart-vm", p.ActionId);
        Assert.Equal("Restart VM", p.DisplayName);
        Assert.Equal("AzureResourceAction", p.ActionType);
        Assert.Equal("C", p.RequiresMode);
        Assert.Equal("actions/restart.json", p.DefinitionFile);
        Assert.NotNull(p.ParametersJson);
        Assert.Null(p.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Mode A deployment — gate blocks proposals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ModeADeployment_ReturnsEmptyProposals()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition());
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "A", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("A"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. SafeActionsEnabled=false — gate blocks proposals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_FeatureDisabled_ReturnsEmptyProposals()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition());
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: false);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Mode C deployment includes all actions (Rule 9: all must be "C")
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ModeCDeployment_IncludesAllActions()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/check.json", MakeActionDefinition("Check Health", "HealthCheck"));
        WriteFile(dir, "actions/restart.json", MakeActionDefinition("Restart", "AzureResourceAction"));
        WriteFile(dir, "actions/scale.json", MakeActionDefinition("Scale Up", "ScaleAction"));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[]
            {
                new PackSafeAction("check-health", "C", "actions/check.json"),
                new PackSafeAction("restart-vm", "C", "actions/restart.json"),
                new PackSafeAction("scale-up", "C", "actions/scale.json")
            }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Proposals.Count);
        Assert.Contains(result.Proposals, p => p.ActionId == "check-health" && p.DisplayName == "Check Health");
        Assert.Contains(result.Proposals, p => p.ActionId == "restart-vm" && p.DisplayName == "Restart");
        Assert.Contains(result.Proposals, p => p.ActionId == "scale-up" && p.DisplayName == "Scale Up");
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. Mode B deployment — gate passes but per-action filter blocks
    //    Rule 9 forces requiresMode="C"; IsModeAtOrBelow("C","B") → false
    //    so Mode B deployments yield zero proposals.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ModeBDeployment_ReturnsEmptyBecausePerActionFilter()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition("Restart", "AzureResourceAction"));
        WriteFile(dir, "actions/scale.json", MakeActionDefinition("Scale Up", "ScaleAction"));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[]
            {
                new PackSafeAction("restart-vm", "C", "actions/restart.json"),
                new PackSafeAction("scale-up", "C", "actions/scale.json")
            }));

        var proposer = CreateProposer(deploymentMode: "B", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        // Pack is valid and eligible (MinimumMode A ≤ B), but both actions
        // require Mode C which is above deployment Mode B → filtered out.
        Assert.Empty(result.Errors);
        Assert.Empty(result.Proposals);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. Invalid pack manifest (name-directory mismatch) is skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_NameMismatch_SkipsPack()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition());
        // manifest name "k8s-pod" does not match directory name "azure-vm"
        WritePackJson(dir, MakeManifest(
            "k8s-pod",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        // pack is invalid (name mismatch) so it's excluded from eligible packs
        Assert.Empty(result.Proposals);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. Multiple packs with actions — proposals aggregated
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_MultiplePacks_AggregatesProposals()
    {
        var dir1 = CreatePackDirectory("azure-vm");
        WriteFile(dir1, "actions/restart.json", MakeActionDefinition("Restart VM", "AzureResourceAction"));
        WritePackJson(dir1, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var dir2 = CreatePackDirectory("k8s-pod");
        WriteFile(dir2, "actions/rollback.json", MakeActionDefinition("Rollback Pod", "K8sAction"));
        WritePackJson(dir2, MakeManifest(
            "k8s-pod",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("rollback-pod", "C", "actions/rollback.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Proposals.Count);
        Assert.Contains(result.Proposals, p => p.PackName == "azure-vm" && p.ActionId == "restart-vm");
        Assert.Contains(result.Proposals, p => p.PackName == "k8s-pod" && p.ActionId == "rollback-pod");
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. Null definition file — fallback proposal with Id/unknown
    //    Rule 14 would reject a specified-but-missing file, so we
    //    test with DefinitionFile = null to keep the pack valid.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_NullDefinitionFile_ReturnsFallbackProposal()
    {
        var dir = CreatePackDirectory("azure-vm");
        // No definition file specified — proposer returns fallback defaults
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", null) }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        // DefinitionFile is null — proposer skips file read, uses fallback:
        // displayName = action.Id, actionType = "unknown", parametersJson = null
        Assert.Empty(result.Errors);
        Assert.Single(result.Proposals);
        var p = result.Proposals[0];
        Assert.Equal("restart-vm", p.ActionId);
        Assert.Equal("restart-vm", p.DisplayName); // fallback to Id
        Assert.Equal("unknown", p.ActionType);      // fallback
        Assert.Null(p.ParametersJson);
        Assert.Null(p.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. Empty packs directory — empty proposals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_EmptyPacksDir_ReturnsEmptyProposals()
    {
        // _tempRoot exists but has no pack subdirectories
        var proposer = CreateProposer(deploymentMode: "B", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. Action definition with all fields — correctly parsed
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_FullDefinition_ParsesAllFields()
    {
        var dir = CreatePackDirectory("azure-vm");

        var definition = JsonSerializer.Serialize(new
        {
            displayName = "Scale Out VMSS",
            actionType = "AzureScaleAction",
            parameters = new
            {
                resourceId = "/subscriptions/{subId}/resourceGroups/{rg}/providers/Microsoft.Compute/virtualMachineScaleSets/{vmss}",
                instanceCount = 5,
                tier = "Standard"
            }
        }, new JsonSerializerOptions { WriteIndented = true });

        WriteFile(dir, "actions/scale-out.json", definition);
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("scale-out", "C", "actions/scale-out.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Empty(result.Errors);
        Assert.Single(result.Proposals);

        var p = result.Proposals[0];
        Assert.Equal("Scale Out VMSS", p.DisplayName);
        Assert.Equal("AzureScaleAction", p.ActionType);
        Assert.Equal("C", p.RequiresMode);
        Assert.NotNull(p.ParametersJson);

        // Verify the parameters JSON is valid and contains expected fields
        using var paramsDoc = JsonDocument.Parse(p.ParametersJson!);
        Assert.True(paramsDoc.RootElement.TryGetProperty("resourceId", out _));
        Assert.True(paramsDoc.RootElement.TryGetProperty("instanceCount", out var ic));
        Assert.Equal(5, ic.GetInt32());
        Assert.True(paramsDoc.RootElement.TryGetProperty("tier", out var tier));
        Assert.Equal("Standard", tier.GetString());
    }
}
