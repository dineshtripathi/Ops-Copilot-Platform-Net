using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackEvidenceExecutor"/> — the Mode-B+ evidence execution logic.
/// Mocks <see cref="IObservabilityQueryExecutor"/>, <see cref="IPackCatalog"/>,
/// <see cref="IPackFileReader"/>, and <see cref="ITenantWorkspaceResolver"/>
/// (MockBehavior.Strict).
/// </summary>
public sealed class PackEvidenceExecutorTests
{
    private const string TestTenantId    = "tenant-unit-test";
    private const string TestWorkspaceId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    // ── Helpers ────────────────────────────────────────────────

    private static LoadedPack MakePack(
        string name,
        bool isValid = true,
        string minimumMode = "A",
        IReadOnlyList<EvidenceCollector>? evidenceCollectors = null)
    {
        var manifest = new PackManifest(
            Name: name,
            Version: "1.0.0",
            Description: $"Test {name}",
            ResourceTypes: new[] { "Microsoft.Compute/virtualMachines" },
            MinimumMode: minimumMode,
            EvidenceCollectors: evidenceCollectors ?? Array.Empty<EvidenceCollector>(),
            Runbooks: Array.Empty<PackRunbook>(),
            SafeActions: Array.Empty<PackSafeAction>());

        var validation = new PackValidationResult(isValid, isValid ? Array.Empty<string>() : new[] { "error" });
        return new LoadedPack(manifest, $"/packs/{name}", validation);
    }

    private static IConfiguration BuildConfig(
        string deploymentMode = "B",
        string evidenceEnabled = "true",
        string? maxRows = null,
        string? maxChars = null)
    {
        var data = new Dictionary<string, string?>
        {
            ["Packs:DeploymentMode"]          = deploymentMode,
            ["Packs:EvidenceExecutionEnabled"] = evidenceEnabled
        };

        if (maxRows  is not null) data["Packs:EvidenceMaxRows"]  = maxRows;
        if (maxChars is not null) data["Packs:EvidenceMaxChars"] = maxChars;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    private static (
        PackEvidenceExecutor           Executor,
        Mock<IObservabilityQueryExecutor> QueryExecutor,
        Mock<IPackCatalog>             Catalog,
        Mock<IPackFileReader>          FileReader,
        Mock<ITenantWorkspaceResolver> WorkspaceResolver,
        Mock<IPacksTelemetry>          Telemetry)
        CreateExecutor(
            IConfiguration?             config          = null,
            WorkspaceResolutionResult?  workspaceResult = null)
    {
        var queryExecutor     = new Mock<IObservabilityQueryExecutor>(MockBehavior.Strict);
        var catalog           = new Mock<IPackCatalog>(MockBehavior.Strict);
        var fileReader        = new Mock<IPackFileReader>(MockBehavior.Strict);
        var workspaceResolver = new Mock<ITenantWorkspaceResolver>(MockBehavior.Strict);
        var telemetry         = new Mock<IPacksTelemetry>(MockBehavior.Loose);

        var cfg  = config ?? BuildConfig();
        var wsResult = workspaceResult
            ?? new WorkspaceResolutionResult(true, TestWorkspaceId, null);

        workspaceResolver.Setup(r => r.Resolve(It.IsAny<string>())).Returns(wsResult);

        var executor = new PackEvidenceExecutor(
            queryExecutor.Object,
            catalog.Object,
            fileReader.Object,
            workspaceResolver.Object,
            cfg,
            NullLogger<PackEvidenceExecutor>.Instance,
            telemetry.Object);

        return (executor, queryExecutor, catalog, fileReader, workspaceResolver, telemetry);
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. Mode A → evidence execution skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ModeA_SkipsExecution()
    {
        var config = BuildConfig(evidenceEnabled: "true");
        var (executor, _, _, _, _, _) = CreateExecutor(config);

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("A", TestTenantId));

        Assert.Empty(result.EvidenceItems);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Feature disabled → evidence execution skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_FeatureDisabled_SkipsExecution()
    {
        var config = BuildConfig(evidenceEnabled: "false");
        var (executor, _, _, _, _, _) = CreateExecutor(config);

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Empty(result.EvidenceItems);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Mode B + enabled → executes evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ModeBEnabled_ExecutesEvidence()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("Perf | where CounterName == 'cpu'");

        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, "Perf | where CounterName == 'cpu'", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(true, "[{\"cpu\":80}]", 1, null));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Single(result.EvidenceItems);
        var item = result.EvidenceItems[0];
        Assert.Equal("azure-vm", item.PackName);
        Assert.Equal("ec1", item.CollectorId);
        Assert.Equal("azure-monitor", item.ConnectorName);
        Assert.Equal("queries/cpu.kql", item.QueryFile);
        Assert.Equal("Perf | where CounterName == 'cpu'", item.QueryContent);
        Assert.Equal("[{\"cpu\":80}]", item.ResultJson);
        Assert.Equal(1, item.RowCount);
        Assert.Null(item.ErrorMessage);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Catalog throws → error in result
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_CatalogThrows_ReturnsError()
    {
        var config = BuildConfig();
        var (executor, _, catalog, _, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("Catalog boom"));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Empty(result.EvidenceItems);
        Assert.Single(result.Errors);
        Assert.Contains("Catalog boom", result.Errors[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. No eligible packs → empty result
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_NoEligiblePacks_ReturnsEmpty()
    {
        var pack = MakePack("future-pack", minimumMode: "C",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "C", "q.kql") });
        var config = BuildConfig();
        var (executor, _, catalog, _, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Empty(result.EvidenceItems);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. Invalid packs are skipped
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_InvalidPack_IsSkipped()
    {
        var invalidPack = MakePack("bad-pack", isValid: false,
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "q.kql") });
        var config = BuildConfig();
        var (executor, _, catalog, _, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { invalidPack });

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Empty(result.EvidenceItems);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. No eligible collectors (collector mode above deployment) → empty
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_NoEligibleCollectors_ReturnsEmpty()
    {
        var pack = MakePack("azure-vm", minimumMode: "A",
            evidenceCollectors: new[] { new EvidenceCollector("ec-c", "C", "q.kql") });
        var config = BuildConfig();
        var (executor, _, catalog, _, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Empty(result.EvidenceItems);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. Query file not found → error + evidence item with error
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_QueryFileNotFound_AddsErrorAndItem()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/missing.kql") });
        var config = BuildConfig();
        var (executor, _, catalog, fileReader, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "queries/missing.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((string?)null);

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Single(result.EvidenceItems);
        Assert.Single(result.Errors);
        var item = result.EvidenceItems[0];
        Assert.Equal("azure-vm", item.PackName);
        Assert.Equal("ec1", item.CollectorId);
        Assert.Null(item.QueryContent);
        Assert.Null(item.ResultJson);
        Assert.Contains("not found", item.ErrorMessage!);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. Query executor returns error → error + evidence item with error
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_QueryExecutorReturnsError_AddsErrorAndItem()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("Perf | where CounterName == 'cpu'");
        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, "Perf | where CounterName == 'cpu'", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(false, null, 0, "Workspace not found", ErrorCode: "azure_not_found"));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Single(result.EvidenceItems);
        Assert.Single(result.Errors);
        var item = result.EvidenceItems[0];
        Assert.Equal("Perf | where CounterName == 'cpu'", item.QueryContent);
        Assert.Null(item.ResultJson);
        Assert.Contains("Workspace not found", item.ErrorMessage!);
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. Query execution fails → item with error message
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_QueryFails_AddsErrorToItemAndErrorList()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("bad query");

        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, "bad query", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(false, null, 0, "Syntax error"));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Single(result.EvidenceItems);
        var item = result.EvidenceItems[0];
        Assert.Equal("Syntax error", item.ErrorMessage);
        Assert.Single(result.Errors);
        Assert.Contains("Syntax error", result.Errors[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. Result truncation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_LargeResult_IsTruncated()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig(maxChars: "100");
        var (executor, queryExecutor, catalog, fileReader, _, _) = CreateExecutor(config);

        var largeJson = new string('X', 500);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("Perf query");

        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, "Perf query", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(true, largeJson, 10, null));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Single(result.EvidenceItems);
        Assert.Equal(100, result.EvidenceItems[0].ResultJson!.Length);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. Multiple packs & mixed results
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_MultiplePacks_AggregatesResults()
    {
        var pack1 = MakePack("vm-pack", minimumMode: "A",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "q1.kql") });
        var pack2 = MakePack("k8s-pack", minimumMode: "B",
            evidenceCollectors: new[] { new EvidenceCollector("ec2", "B", "q2.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack1, pack2 });
        fileReader.Setup(f => f.ReadFileAsync("/packs/vm-pack", "q1.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("VM query");
        fileReader.Setup(f => f.ReadFileAsync("/packs/k8s-pack", "q2.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("K8s query");

        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, "VM query", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(true, "[{\"vm\":1}]", 1, null));
        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, "K8s query", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(true, "[{\"pod\":1}]", 1, null));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Equal(2, result.EvidenceItems.Count);
        Assert.Contains(result.EvidenceItems, e => e.PackName == "vm-pack");
        Assert.Contains(result.EvidenceItems, e => e.PackName == "k8s-pack");
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. Collector exception → error added, processing continues
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_CollectorThrows_AddsErrorButContinues()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[]
            {
                new EvidenceCollector("ec-fail", "A", "fail.kql"),
                new EvidenceCollector("ec-ok", "A", "ok.kql")
            });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "fail.kql", It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new IOException("Disk error"));
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "ok.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("OK query");

        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, "OK query", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(true, "[{\"ok\":1}]", 1, null));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Equal(2, result.EvidenceItems.Count);
        Assert.Single(result.Errors);
        Assert.Contains("Disk error", result.Errors[0]);

        var okItem = result.EvidenceItems.Single(e => e.CollectorId == "ec-ok");
        Assert.Equal("[{\"ok\":1}]", okItem.ResultJson);
        Assert.Null(okItem.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. Mode C → Mode B collectors are eligible
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ModeC_IncludesModeAAndBCollectors()
    {
        var pack = MakePack("azure-vm", minimumMode: "A",
            evidenceCollectors: new[]
            {
                new EvidenceCollector("ec-a", "A", "q-a.kql"),
                new EvidenceCollector("ec-b", "B", "q-b.kql"),
                new EvidenceCollector("ec-c", "C", "q-c.kql")
            });
        var config = BuildConfig(deploymentMode: "C", evidenceEnabled: "true");
        var (executor, queryExecutor, catalog, fileReader, _, _) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "q-a.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("query A");
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "q-b.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("query B");
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "q-c.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("query C");

        queryExecutor.Setup(q => q.ExecuteQueryAsync(TestWorkspaceId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new QueryExecutionResult(true, "[]", 0, null));

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("C", TestTenantId));

        Assert.Equal(3, result.EvidenceItems.Count);
        Assert.Contains(result.EvidenceItems, e => e.CollectorId == "ec-a");
        Assert.Contains(result.EvidenceItems, e => e.CollectorId == "ec-b");
        Assert.Contains(result.EvidenceItems, e => e.CollectorId == "ec-c");
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 15. Missing workspace → per-item errors for eligible collectors
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_MissingWorkspace_ProducesPerItemErrors()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[]
            {
                new EvidenceCollector("ec1", "A", "cpu.kql"),
                new EvidenceCollector("ec2", "A", "mem.kql")
            });
        var config        = BuildConfig();
        var wsFailResult  = new WorkspaceResolutionResult(false, null, "missing_workspace");
        var (executor, _, catalog, _, _, _) = CreateExecutor(config, wsFailResult);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        // Both collectors get a per-item error
        Assert.Equal(2, result.EvidenceItems.Count);
        Assert.All(result.EvidenceItems, item =>
        {
            Assert.Equal("Workspace not configured for tenant", item.ErrorMessage);
            Assert.Null(item.ResultJson);
            Assert.Equal(0, item.RowCount);
        });
        // Per-item error strings contain the error code
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Contains("missing_workspace", e));
    }

    // ═══════════════════════════════════════════════════════════════
    // 16. Workspace not allowlisted → per-item errors
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_WorkspaceNotAllowlisted_ProducesPerItemErrors()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[]
            {
                new EvidenceCollector("ec1", "A", "cpu.kql")
            });
        var config       = BuildConfig();
        var wsFailResult = new WorkspaceResolutionResult(false, null, "workspace_not_allowlisted");
        var (executor, _, catalog, _, _, _) = CreateExecutor(config, wsFailResult);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });

        var result = await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        Assert.Single(result.EvidenceItems);
        Assert.Equal("Workspace not in allowlist", result.EvidenceItems[0].ErrorMessage);
        Assert.Single(result.Errors);
        Assert.Contains("workspace_not_allowlisted", result.Errors[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  TELEMETRY VERIFICATION TESTS
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    // T1. Mode A → RecordEvidenceSkipped, no attempt recorded
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_ModeA_RecordsEvidenceSkipped()
    {
        var config = BuildConfig(evidenceEnabled: "true");
        var (executor, _, _, _, _, telemetry) = CreateExecutor(config);

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("A", TestTenantId));

        telemetry.Verify(t => t.RecordEvidenceSkipped("A", TestTenantId), Times.Once);
        telemetry.Verify(
            t => t.RecordEvidenceAttempt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════
    // T2. Feature disabled → RecordEvidenceSkipped, no attempt
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_FeatureDisabled_RecordsEvidenceSkipped()
    {
        var config = BuildConfig(evidenceEnabled: "false");
        var (executor, _, _, _, _, telemetry) = CreateExecutor(config);

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        telemetry.Verify(t => t.RecordEvidenceSkipped("B", TestTenantId), Times.Once);
        telemetry.Verify(
            t => t.RecordEvidenceAttempt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════
    // T3. Mode B success → RecordEvidenceAttempt + RecordCollectorSuccess
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_ModeBSuccess_RecordsAttemptAndCollectorSuccess()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, telemetry) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync("/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
                  .ReturnsAsync("Perf | where CounterName == 'cpu'");
        queryExecutor.Setup(q => q.ExecuteQueryAsync(
                TestWorkspaceId, "Perf | where CounterName == 'cpu'", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecutionResult(true, "[{\"cpu\":80}]", 1, null));

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId, CorrelationId: "corr-001"));

        telemetry.Verify(t => t.RecordEvidenceAttempt("B", TestTenantId, "corr-001"), Times.Once);
        telemetry.Verify(t => t.RecordCollectorSuccess("azure-vm", "ec1", TestTenantId, "corr-001"), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // T4. Workspace resolution failed → RecordWorkspaceResolutionFailed
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_WorkspaceResolutionFailed_RecordsMetric()
    {
        var config       = BuildConfig();
        var wsFailResult = new WorkspaceResolutionResult(false, null, "missing_workspace");
        var (executor, _, catalog, _, _, telemetry) = CreateExecutor(config, wsFailResult);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<LoadedPack>());

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId, CorrelationId: "corr-ws"));

        telemetry.Verify(
            t => t.RecordWorkspaceResolutionFailed(TestTenantId, "missing_workspace", "corr-ws"),
            Times.Once);
        telemetry.Verify(
            t => t.RecordEvidenceAttempt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════
    // T5. Query file not found → RecordQueryBlocked
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_QueryFileNotFound_RecordsQueryBlocked()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/missing.kql") });
        var config = BuildConfig();
        var (executor, _, catalog, fileReader, _, telemetry) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "queries/missing.kql", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId, CorrelationId: "corr-qb"));

        telemetry.Verify(
            t => t.RecordQueryBlocked("azure-vm", "ec1", TestTenantId, "corr-qb"), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // T6. Query execution returns failure → RecordQueryFailed
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_QueryExecutionFails_RecordsQueryFailed()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, telemetry) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
            .ReturnsAsync("bad query");
        queryExecutor.Setup(q => q.ExecuteQueryAsync(
                TestWorkspaceId, "bad query", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecutionResult(false, null, 0, "Syntax error"));

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId, CorrelationId: "corr-qf"));

        telemetry.Verify(
            t => t.RecordQueryFailed("azure-vm", "ec1", TestTenantId, "Syntax error", "corr-qf"),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // T7. Truncated result → RecordCollectorTruncated + success
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_TruncatedResult_RecordsCollectorTruncated()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig(maxChars: "100");
        var (executor, queryExecutor, catalog, fileReader, _, telemetry) = CreateExecutor(config);

        var largeJson = new string('X', 500);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Perf query");
        queryExecutor.Setup(q => q.ExecuteQueryAsync(
                TestWorkspaceId, "Perf query", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecutionResult(true, largeJson, 10, null));

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId, CorrelationId: "corr-tr"));

        telemetry.Verify(
            t => t.RecordCollectorTruncated("azure-vm", "ec1", "max_chars", "corr-tr"), Times.Once);
        telemetry.Verify(
            t => t.RecordCollectorSuccess("azure-vm", "ec1", TestTenantId, "corr-tr"), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // T8. Collector throws → RecordCollectorFailure
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_CollectorThrows_RecordsCollectorFailure()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, _, catalog, fileReader, _, telemetry) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk error"));

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId, CorrelationId: "corr-cf"));

        telemetry.Verify(
            t => t.RecordCollectorFailure("azure-vm", "ec1", TestTenantId, "exception", "corr-cf"),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // T9. Query timeout → RecordQueryTimeout
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_QueryTimeout_RecordsQueryTimeout()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, telemetry) = CreateExecutor(config);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Perf | where CounterName == 'cpu'");
        queryExecutor.Setup(q => q.ExecuteQueryAsync(
                TestWorkspaceId, "Perf | where CounterName == 'cpu'", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId, CorrelationId: "corr-to"), cts.Token);

        telemetry.Verify(
            t => t.RecordQueryTimeout("azure-vm", "ec1", TestTenantId, "corr-to"), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // T10. Small result → no truncation metric recorded
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_SmallResult_DoesNotRecordCollectorTruncated()
    {
        var pack = MakePack("azure-vm",
            evidenceCollectors: new[] { new EvidenceCollector("ec1", "A", "queries/cpu.kql") });
        var config = BuildConfig();
        var (executor, queryExecutor, catalog, fileReader, _, telemetry) = CreateExecutor(config);

        catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { pack });
        fileReader.Setup(f => f.ReadFileAsync(
                "/packs/azure-vm", "queries/cpu.kql", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Perf query");
        queryExecutor.Setup(q => q.ExecuteQueryAsync(
                TestWorkspaceId, "Perf query", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecutionResult(true, "[{\"cpu\":80}]", 1, null));

        await executor.ExecuteAsync(
            new PackEvidenceExecutionRequest("B", TestTenantId));

        telemetry.Verify(
            t => t.RecordCollectorTruncated(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
        telemetry.Verify(
            t => t.RecordCollectorSuccess("azure-vm", "ec1", TestTenantId, It.IsAny<string>()),
            Times.Once);
    }
}
