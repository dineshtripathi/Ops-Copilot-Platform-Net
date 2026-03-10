using System.Text.Json;
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
        bool safeActionsEnabled = true,
        IToolAllowlistPolicy? toolPolicy = null)
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
        var scopeFactory = CreateScopeFactory(toolPolicy);

        return new PackSafeActionProposer(
            catalog,
            fileReader,
            config,
            NullLogger<PackSafeActionProposer>.Instance,
            telemetry.Object,
            scopeFactory.Object);
    }

    private static Mock<IServiceScopeFactory> CreateScopeFactory(IToolAllowlistPolicy? toolPolicy = null)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IToolAllowlistPolicy)))
            .Returns(toolPolicy);

        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);

        return scopeFactory;
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
        Assert.True(p.IsExecutableNow);
        Assert.Null(p.ExecutionBlockedReason);
        Assert.Null(p.DefinitionValidationErrorCode);
        Assert.Null(p.DefinitionValidationMessage);
        Assert.NotNull(p.OperatorPreview);
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
    // 4. Mode C deployment includes all actions
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
        Assert.All(result.Proposals, p =>
        {
            Assert.True(p.IsExecutableNow);
            Assert.Null(p.ExecutionBlockedReason);
            Assert.Null(p.DefinitionValidationErrorCode);
            Assert.Null(p.DefinitionValidationMessage);
            Assert.NotNull(p.OperatorPreview);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. Mode B deployment — actions above mode returned as non-executable
    //    IsModeAtOrBelow("C","B") → false
    //    so Mode B deployments include proposals with IsExecutableNow=false.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ModeBDeployment_ReturnsNotExecutableProposals()
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

        // Pack is valid and eligible (MinimumMode A ≤ B). Both actions require
        // Mode C (above deployment Mode B) → included as recommendations with
        // IsExecutableNow=false.
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Proposals.Count);
        Assert.All(result.Proposals, p =>
        {
            Assert.False(p.IsExecutableNow);
            Assert.Equal("requires_higher_mode", p.ExecutionBlockedReason);
            Assert.Null(p.DefinitionValidationErrorCode);
            Assert.Null(p.DefinitionValidationMessage);
            Assert.NotNull(p.OperatorPreview);
        });
        Assert.Contains(result.Proposals, p => p.ActionId == "restart-vm" && p.DisplayName == "Restart");
        Assert.Contains(result.Proposals, p => p.ActionId == "scale-up" && p.DisplayName == "Scale Up");
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
        Assert.All(result.Proposals, p =>
        {
            Assert.True(p.IsExecutableNow);
            Assert.Null(p.ExecutionBlockedReason);
            Assert.Null(p.DefinitionValidationErrorCode);
            Assert.Null(p.DefinitionValidationMessage);
            Assert.NotNull(p.OperatorPreview);
        });
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
        Assert.True(p.IsExecutableNow);
        Assert.Null(p.ExecutionBlockedReason);
        Assert.Null(p.DefinitionValidationErrorCode);
        Assert.Null(p.DefinitionValidationMessage);
        Assert.Null(p.OperatorPreview);
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
        Assert.True(p.IsExecutableNow);
        Assert.Null(p.ExecutionBlockedReason);
        Assert.Null(p.DefinitionValidationErrorCode);
        Assert.Null(p.DefinitionValidationMessage);
        Assert.NotNull(p.OperatorPreview);
        Assert.Contains("== Operator Card ==", p.OperatorPreview);

        // Verify the parameters JSON is valid and contains expected fields
        using var paramsDoc = JsonDocument.Parse(p.ParametersJson!);
        Assert.True(paramsDoc.RootElement.TryGetProperty("resourceId", out _));
        Assert.True(paramsDoc.RootElement.TryGetProperty("instanceCount", out var ic));
        Assert.Equal(5, ic.GetInt32());
        Assert.True(paramsDoc.RootElement.TryGetProperty("tier", out var tier));
        Assert.Equal("Standard", tier.GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. Mode B with mixed-mode actions — A/B executable, C not
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ModeBMixedActions_SetsCorrectEligibility()
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
                new PackSafeAction("check-health", "A", "actions/check.json"),
                new PackSafeAction("restart-vm", "B", "actions/restart.json"),
                new PackSafeAction("scale-up", "C", "actions/scale.json")
            }));

        var proposer = CreateProposer(deploymentMode: "B", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("B"));

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Proposals.Count);

        var checkHealth = result.Proposals.Single(p => p.ActionId == "check-health");
        Assert.True(checkHealth.IsExecutableNow);
        Assert.Null(checkHealth.ExecutionBlockedReason);
        Assert.Null(checkHealth.DefinitionValidationErrorCode);
        Assert.Null(checkHealth.DefinitionValidationMessage);
        Assert.NotNull(checkHealth.OperatorPreview);

        var restart = result.Proposals.Single(p => p.ActionId == "restart-vm");
        Assert.True(restart.IsExecutableNow);
        Assert.Null(restart.ExecutionBlockedReason);
        Assert.Null(restart.DefinitionValidationErrorCode);
        Assert.Null(restart.DefinitionValidationMessage);
        Assert.NotNull(restart.OperatorPreview);

        var scaleUp = result.Proposals.Single(p => p.ActionId == "scale-up");
        Assert.False(scaleUp.IsExecutableNow);
        Assert.Equal("requires_higher_mode", scaleUp.ExecutionBlockedReason);
        Assert.Null(scaleUp.DefinitionValidationErrorCode);
        Assert.Null(scaleUp.DefinitionValidationMessage);
        Assert.NotNull(scaleUp.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. Governance — policy allows action type
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_GovernanceAllowed_SetsGovernanceFields()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition(
            "Restart VM", "AzureResourceAction"));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool("tenant-integ", "AzureResourceAction"))
              .Returns(PolicyDecision.Allow());

        var proposer = CreateProposer(deploymentMode: "C", toolPolicy: policy.Object);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.True(item.GovernanceAllowed);
        Assert.Null(item.GovernanceReasonCode);
        Assert.Null(item.GovernanceMessage);
        Assert.Null(item.DefinitionValidationErrorCode);
        Assert.Null(item.DefinitionValidationMessage);
        Assert.NotNull(item.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. Governance — policy denies action type
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_GovernanceDenied_SetsGovernanceFields()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition(
            "Restart VM", "AzureResourceAction"));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool("tenant-integ", "AzureResourceAction"))
              .Returns(PolicyDecision.Deny("tool_not_in_allowlist", "AzureResourceAction is not permitted."));

        var proposer = CreateProposer(deploymentMode: "C", toolPolicy: policy.Object);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.False(item.GovernanceAllowed);
        Assert.Equal("tool_not_in_allowlist", item.GovernanceReasonCode);
        Assert.Equal("AzureResourceAction is not permitted.", item.GovernanceMessage);
        Assert.Null(item.DefinitionValidationErrorCode);
        Assert.Null(item.DefinitionValidationMessage);
        Assert.NotNull(item.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. Governance — no policy registered → null governance fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_NoPolicyRegistered_GovernanceFieldsNull()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition(
            "Restart VM", "AzureResourceAction"));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        // default CreateProposer — no policy registered
        var proposer = CreateProposer(deploymentMode: "C");

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Null(item.GovernanceAllowed);
        Assert.Null(item.GovernanceReasonCode);
        Assert.Null(item.GovernanceMessage);
        Assert.Null(item.DefinitionValidationErrorCode);
        Assert.Null(item.DefinitionValidationMessage);
        Assert.NotNull(item.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 15. Governance — null tenantId → null governance fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_NullTenantId_GovernanceFieldsNull()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition(
            "Restart VM", "AzureResourceAction"));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", "actions/restart.json") }));

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        // Policy is registered but tenantId is null — governance should be skipped
        var proposer = CreateProposer(deploymentMode: "C", toolPolicy: policy.Object);

        var request = new PackSafeActionProposalRequest("C", null, "corr-integ-001");
        var result = await proposer.ProposeAsync(request);

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Null(item.GovernanceAllowed);
        Assert.Null(item.GovernanceReasonCode);
        Assert.Null(item.GovernanceMessage);
        Assert.Null(item.DefinitionValidationErrorCode);
        Assert.Null(item.DefinitionValidationMessage);
        Assert.NotNull(item.OperatorPreview);
        // Policy should never be called when tenantId is null
        policy.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════
    // 16. Governance — mixed results across multiple actions
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_GovernanceMixedActions_EachActionGetsOwnDecision()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/check.json", MakeActionDefinition("Check Health", "HealthCheck"));
        WriteFile(dir, "actions/restart.json", MakeActionDefinition("Restart", "AzureResourceAction"));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[]
            {
                new PackSafeAction("check-health", "C", "actions/check.json"),
                new PackSafeAction("restart-vm", "C", "actions/restart.json")
            }));

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool("tenant-integ", "HealthCheck"))
              .Returns(PolicyDecision.Allow());
        policy.Setup(p => p.CanUseTool("tenant-integ", "AzureResourceAction"))
              .Returns(PolicyDecision.Deny("tool_not_in_allowlist", "Not permitted."));

        var proposer = CreateProposer(deploymentMode: "C", toolPolicy: policy.Object);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Equal(2, result.Proposals.Count);

        var check = result.Proposals.Single(p => p.ActionId == "check-health");
        Assert.True(check.GovernanceAllowed);
        Assert.Null(check.GovernanceReasonCode);
        Assert.Null(check.DefinitionValidationErrorCode);
        Assert.Null(check.DefinitionValidationMessage);
        Assert.NotNull(check.OperatorPreview);

        var restart = result.Proposals.Single(p => p.ActionId == "restart-vm");
        Assert.False(restart.GovernanceAllowed);
        Assert.Equal("tool_not_in_allowlist", restart.GovernanceReasonCode);
        Assert.Null(restart.DefinitionValidationErrorCode);
        Assert.Null(restart.DefinitionValidationMessage);
        Assert.NotNull(restart.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 17. Governance — null definition file (fallback) still enriched
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_NullDefinitionFileWithPolicy_GovernanceUsesUnknownActionType()
    {
        var dir = CreatePackDirectory("azure-vm");
        // No definition file — actionType falls back to "unknown"
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "C", null) }));

        var policy = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.CanUseTool("tenant-integ", "unknown"))
              .Returns(PolicyDecision.Allow());

        var proposer = CreateProposer(deploymentMode: "C", toolPolicy: policy.Object);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];
        Assert.Equal("unknown", item.ActionType);
        Assert.True(item.GovernanceAllowed);
        Assert.Null(item.GovernanceReasonCode);
        Assert.Null(item.DefinitionValidationErrorCode);
        Assert.Null(item.DefinitionValidationMessage);
        Assert.Null(item.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 18. Validation — invalid JSON definition
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_InvalidJsonDefinition_ReturnsParseError()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", "{ NOT VALID JSON }}}");
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "B", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var p = result.Proposals[0];
        Assert.Equal("parse_error", p.DefinitionValidationErrorCode);
        Assert.NotNull(p.DefinitionValidationMessage);
        Assert.Contains("Invalid JSON", p.DefinitionValidationMessage);
        Assert.False(p.IsExecutableNow);
        Assert.Equal("invalid_definition", p.ExecutionBlockedReason);
        Assert.NotNull(p.OperatorPreview);
        Assert.Contains("Valid  : no", p.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 19. Validation — missing displayName
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_MissingDisplayName_ReturnsMissingDisplayNameError()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", """{"actionType":"AzureResourceAction"}""");
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "B", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var p = result.Proposals[0];
        Assert.Equal("missing_display_name", p.DefinitionValidationErrorCode);
        Assert.Contains("displayName", p.DefinitionValidationMessage!);
        Assert.False(p.IsExecutableNow);
        Assert.Equal("invalid_definition", p.ExecutionBlockedReason);
        Assert.NotNull(p.OperatorPreview);
        Assert.Contains("Valid  : no", p.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 20. Validation — missing actionType
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_MissingActionType_ReturnsMissingActionTypeError()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", """{"displayName":"Restart VM"}""");
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "B", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var p = result.Proposals[0];
        Assert.Equal("missing_action_type", p.DefinitionValidationErrorCode);
        Assert.Contains("actionType", p.DefinitionValidationMessage!);
        Assert.False(p.IsExecutableNow);
        Assert.Equal("invalid_definition", p.ExecutionBlockedReason);
        Assert.NotNull(p.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 21. Validation — id mismatch
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_IdMismatch_ReturnsIdMismatchError()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition("Restart VM", "AzureResourceAction") + "");
        // Write a definition with an explicit id that doesn't match
        var defJson = """{"displayName":"Restart VM","actionType":"AzureResourceAction","id":"wrong-id"}""";
        WriteFile(dir, "actions/restart.json", defJson);
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "B", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var p = result.Proposals[0];
        Assert.Equal("id_mismatch", p.DefinitionValidationErrorCode);
        Assert.Contains("wrong-id", p.DefinitionValidationMessage!);
        Assert.Contains("restart-vm", p.DefinitionValidationMessage!);
        Assert.False(p.IsExecutableNow);
        Assert.Equal("invalid_definition", p.ExecutionBlockedReason);
        Assert.NotNull(p.OperatorPreview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 22. Valid definition — Operator Preview format verification
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_ValidDefinition_OperatorPreviewContainsExpectedLines()
    {
        var dir = CreatePackDirectory("azure-vm");
        WriteFile(dir, "actions/restart.json", MakeActionDefinition(
            "Restart VM",
            "AzureResourceAction",
            new { resourceId = "{resourceId}", action = "restart" }));
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "B", "actions/restart.json") }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var p = result.Proposals[0];
        Assert.Null(p.DefinitionValidationErrorCode);
        Assert.NotNull(p.OperatorPreview);

        var preview = p.OperatorPreview!;
        Assert.Contains("== Operator Card ==", preview);
        Assert.Contains("Action : Restart VM", preview);
        Assert.Contains("Type   : AzureResourceAction", preview);
        Assert.Contains("Params : action, resourceId", preview);
        Assert.Contains("Valid  : yes", preview);
    }

    // ═══════════════════════════════════════════════════════════════
    // 23. Mixed valid/invalid definitions — correct per-action results
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_MixedValidInvalidDefinitions_ReturnsCorrectPerActionValidation()
    {
        var dir = CreatePackDirectory("azure-vm");
        // Valid definition
        WriteFile(dir, "actions/check.json", MakeActionDefinition("Check Health", "AzureResourceAction"));
        // Invalid definition — missing displayName
        WriteFile(dir, "actions/restart.json", """{"actionType":"AzureResourceAction"}""");
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[]
            {
                new PackSafeAction("check-health", "B", "actions/check.json"),
                new PackSafeAction("restart-vm", "B", "actions/restart.json")
            }));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Equal(2, result.Proposals.Count);

        var check = result.Proposals.Single(p => p.ActionId == "check-health");
        Assert.Null(check.DefinitionValidationErrorCode);
        Assert.Null(check.DefinitionValidationMessage);
        Assert.NotNull(check.OperatorPreview);
        Assert.Contains("Valid  : yes", check.OperatorPreview!);
        Assert.True(check.IsExecutableNow);

        var restart = result.Proposals.Single(p => p.ActionId == "restart-vm");
        Assert.Equal("missing_display_name", restart.DefinitionValidationErrorCode);
        Assert.NotNull(restart.DefinitionValidationMessage);
        Assert.Contains("Valid  : no", restart.OperatorPreview!);
        Assert.False(restart.IsExecutableNow);
        Assert.Equal("invalid_definition", restart.ExecutionBlockedReason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 24. Governance + Validation — both field sets populated
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_GovernanceDeniedPlusInvalidDefinition_BothFieldSetsCorrect()
    {
        var dir = CreatePackDirectory("azure-vm");
        // Invalid definition — missing displayName
        WriteFile(dir, "actions/restart.json", """{"actionType":"AzureResourceAction"}""");
        WritePackJson(dir, MakeManifest(
            "azure-vm",
            minimumMode: "A",
            safeActions: new[] { new PackSafeAction("restart-vm", "B", "actions/restart.json") }));

        var policy = new Mock<IToolAllowlistPolicy>();
        policy.Setup(p => p.CanUseTool("tenant-integ", "AzureResourceAction"))
              .Returns(PolicyDecision.Deny("tool_not_in_allowlist", "AzureResourceAction is not permitted."));

        var proposer = CreateProposer(deploymentMode: "C", safeActionsEnabled: true, toolPolicy: policy.Object);

        var result = await proposer.ProposeAsync(MakeRequest("C"));

        Assert.Single(result.Proposals);
        var item = result.Proposals[0];

        // Validation fields
        Assert.Equal("missing_display_name", item.DefinitionValidationErrorCode);
        Assert.NotNull(item.DefinitionValidationMessage);
        Assert.NotNull(item.OperatorPreview);
        Assert.Contains("Valid  : no", item.OperatorPreview!);

        // Governance fields
        Assert.False(item.GovernanceAllowed);
        Assert.Equal("tool_not_in_allowlist", item.GovernanceReasonCode);

        // Execution blocked
        Assert.False(item.IsExecutableNow);
        Assert.Equal("invalid_definition", item.ExecutionBlockedReason);
    }
}
