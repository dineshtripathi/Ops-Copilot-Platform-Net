using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.BuildingBlocks.Contracts.SafeActions;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackSafeActionRecorder"/> — the Mode-C only
/// safe-action recording logic.  Mocks <see cref="ISafeActionProposalService"/>
/// (MockBehavior.Strict when needed), <see cref="IPacksTelemetry"/> (MockBehavior.Loose).
/// </summary>
public sealed class PackSafeActionRecorderTests
{
    private const string TestTenantId = "tenant-unit-test";

    // ── Helpers ────────────────────────────────────────────────

    private static IConfiguration BuildConfig(
        string deploymentMode = "C",
        string safeActionsEnabled = "true")
    {
        var data = new Dictionary<string, string?>
        {
            ["Packs:DeploymentMode"]    = deploymentMode,
            ["Packs:SafeActionsEnabled"] = safeActionsEnabled
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    private static Mock<IServiceScopeFactory> CreateScopeFactory(
        ISafeActionProposalService? proposalService = null)
    {
        var sp = new Mock<IServiceProvider>(MockBehavior.Strict);
        sp.Setup(s => s.GetService(typeof(ISafeActionProposalService)))
          .Returns(proposalService!);

        var scope = new Mock<IServiceScope>(MockBehavior.Strict);
        scope.SetupGet(s => s.ServiceProvider).Returns(sp.Object);
        scope.Setup(s => s.Dispose());

        var factory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return factory;
    }

    private static (
        PackSafeActionRecorder Recorder,
        Mock<IPacksTelemetry> Telemetry)
        CreateRecorder(
            IConfiguration? config = null,
            Mock<IServiceScopeFactory>? scopeFactory = null)
    {
        config ??= BuildConfig();
        scopeFactory ??= CreateScopeFactory();

        var telemetry = new Mock<IPacksTelemetry>(MockBehavior.Loose);

        var recorder = new PackSafeActionRecorder(
            config,
            NullLogger<PackSafeActionRecorder>.Instance,
            telemetry.Object,
            scopeFactory.Object);

        return (recorder, telemetry);
    }

    private static PackSafeActionProposalItem CreateProposal(
        string packName = "azure-vm",
        string actionId = "sa-restart",
        string actionType = "restart_vm",
        bool isExecutableNow = true,
        string? executionBlockedReason = null,
        bool? governanceAllowed = true,
        string? governanceReasonCode = null,
        string? governanceMessage = null,
        bool? scopeAllowed = true,
        string? scopeReasonCode = null,
        string? scopeMessage = null,
        string? parametersJson = """{"size":"standard"}""",
        string? errorMessage = null)
    {
        return new PackSafeActionProposalItem(
            PackName: packName,
            ActionId: actionId,
            DisplayName: $"Test {actionId}",
            ActionType: actionType,
            RequiresMode: "B",
            DefinitionFile: $"actions/{actionId}.json",
            ParametersJson: parametersJson,
            ErrorMessage: errorMessage,
            IsExecutableNow: isExecutableNow,
            ExecutionBlockedReason: executionBlockedReason,
            GovernanceAllowed: governanceAllowed,
            GovernanceReasonCode: governanceReasonCode,
            GovernanceMessage: governanceMessage,
            ScopeAllowed: scopeAllowed,
            ScopeReasonCode: scopeReasonCode,
            ScopeMessage: scopeMessage);
    }

    private static PackSafeActionRecordRequest MakeRequest(
        string mode = "C",
        string tenantId = TestTenantId,
        Guid? runId = null,
        IReadOnlyList<PackSafeActionProposalItem>? proposals = null)
    {
        return new PackSafeActionRecordRequest(
            DeploymentMode: mode,
            TenantId: tenantId,
            TriageRunId: runId ?? Guid.NewGuid(),
            Proposals: proposals ?? Array.Empty<PackSafeActionProposalItem>());
    }

    // ── Gate tests ────────────────────────────────────────────

    // 1. Mode A → empty, skipped telemetry

    [Fact]
    public async Task RecordAsync_ModeA_ReturnsEmptyWithSkippedTelemetry()
    {
        var config = BuildConfig(deploymentMode: "A");
        var (recorder, telemetry) = CreateRecorder(config);

        var result = await recorder.RecordAsync(MakeRequest("A"));

        Assert.Empty(result.Records);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Errors);
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("gate", "mode_not_c", TestTenantId, null),
            Times.Once);
    }

    // 2. Mode B → empty

    [Fact]
    public async Task RecordAsync_ModeB_ReturnsEmpty()
    {
        var config = BuildConfig(deploymentMode: "B");
        var (recorder, telemetry) = CreateRecorder(config);

        var result = await recorder.RecordAsync(MakeRequest("B"));

        Assert.Empty(result.Records);
        Assert.Equal(0, result.CreatedCount);
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("gate", "mode_not_c", TestTenantId, null),
            Times.Once);
    }

    // 3. Feature disabled → empty

    [Fact]
    public async Task RecordAsync_FeatureDisabled_ReturnsEmpty()
    {
        var config = BuildConfig(deploymentMode: "C", safeActionsEnabled: "false");
        var (recorder, telemetry) = CreateRecorder(config);

        var result = await recorder.RecordAsync(MakeRequest("C"));

        Assert.Empty(result.Records);
        Assert.Equal(0, result.CreatedCount);
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("gate", "feature_disabled", TestTenantId, null),
            Times.Once);
    }

    // 4. Empty proposals → zero counts

    [Fact]
    public async Task RecordAsync_EmptyProposals_ReturnsZeroCounts()
    {
        var (recorder, telemetry) = CreateRecorder();

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: Array.Empty<PackSafeActionProposalItem>()));

        Assert.Empty(result.Records);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Errors);
        // Attempt telemetry should still fire (gates passed)
        telemetry.Verify(
            t => t.RecordSafeActionAttempt("C", TestTenantId, TestTenantId),
            Times.Once);
    }

    // ── Skip tests ────────────────────────────────────────────

    // 5. Non-executable proposal → Skipped

    [Fact]
    public async Task RecordAsync_SkipsNonExecutableProposal()
    {
        var proposal = CreateProposal(
            isExecutableNow: false,
            executionBlockedReason: "mode_too_low");

        var (recorder, telemetry) = CreateRecorder();

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: new[] { proposal }));

        var item = Assert.Single(result.Records);
        Assert.Equal("Skipped", item.Status);
        Assert.Null(item.ActionRecordId);
        Assert.Equal("mode_too_low", item.ErrorMessage);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("azure-vm", "sa-restart", TestTenantId, "not_executable"),
            Times.Once);
    }

    // 6. Governance-denied proposal → Skipped

    [Fact]
    public async Task RecordAsync_SkipsGovernanceDeniedProposal()
    {
        var proposal = CreateProposal(
            governanceAllowed: false,
            governanceReasonCode: "not_allowlisted",
            governanceMessage: "Tool not permitted");

        var (recorder, telemetry) = CreateRecorder();

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: new[] { proposal }));

        var item = Assert.Single(result.Records);
        Assert.Equal("Skipped", item.Status);
        Assert.Null(item.ActionRecordId);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("azure-vm", "sa-restart", TestTenantId, "governance_denied"),
            Times.Once);
    }

    // ── Success tests ─────────────────────────────────────────

    // 7. Happy path — single proposal creates record

    [Fact]
    public async Task RecordAsync_CreatesRecordOnSuccess()
    {
        var proposalItem = CreateProposal();
        var expectedId = Guid.NewGuid();

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId,
                It.IsAny<Guid>(),
                "restart_vm",
                """{"size":"standard"}""",
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SafeActionProposalResponse(expectedId, "None"));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, telemetry) = CreateRecorder(scopeFactory: scopeFactory);

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: new[] { proposalItem }));

        var item = Assert.Single(result.Records);
        Assert.Equal("Created", item.Status);
        Assert.Equal(expectedId, item.ActionRecordId);
        Assert.Equal("restart_vm", item.ActionType);
        Assert.Null(item.ErrorMessage);
        Assert.Null(item.PolicyDenialReasonCode);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Errors);
        svc.VerifyAll();
    }

    // ── Error tests ───────────────────────────────────────────

    // 8. PolicyDeniedException → PolicyDenied status

    [Fact]
    public async Task RecordAsync_PolicyDenied_ReturnsCorrectStatus()
    {
        var proposalItem = CreateProposal();

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId,
                It.IsAny<Guid>(),
                "restart_vm",
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SafeActionProposalDeniedException(
                "action_type_not_allowed",
                "restart_vm is not allowed by policy."));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, telemetry) = CreateRecorder(scopeFactory: scopeFactory);

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: new[] { proposalItem }));

        var item = Assert.Single(result.Records);
        Assert.Equal("PolicyDenied", item.Status);
        Assert.Null(item.ActionRecordId);
        Assert.Equal("action_type_not_allowed", item.PolicyDenialReasonCode);
        Assert.Equal("restart_vm is not allowed by policy.", item.ErrorMessage);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
        Assert.Contains("PolicyDenied", result.Errors[0]);
    }

    // 9. Generic exception → Failed status

    [Fact]
    public async Task RecordAsync_GenericException_ReturnsFailedStatus()
    {
        var proposalItem = CreateProposal();

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId,
                It.IsAny<Guid>(),
                "restart_vm",
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, telemetry) = CreateRecorder(scopeFactory: scopeFactory);

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: new[] { proposalItem }));

        var item = Assert.Single(result.Records);
        Assert.Equal("Failed", item.Status);
        Assert.Null(item.ActionRecordId);
        Assert.Equal("Database connection failed", item.ErrorMessage);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
        Assert.Contains("Failed", result.Errors[0]);
    }

    // ── Aggregation tests ─────────────────────────────────────

    // 10. Mixed outcomes aggregate correctly

    [Fact]
    public async Task RecordAsync_MixedOutcomes_AggregatesCorrectly()
    {
        var createdId = Guid.NewGuid();
        var proposals = new[]
        {
            CreateProposal(packName: "p1", actionId: "a1", actionType: "restart_vm"),
            CreateProposal(packName: "p2", actionId: "a2", actionType: "stop_vm",
                isExecutableNow: false, executionBlockedReason: "mode_too_low"),
            CreateProposal(packName: "p3", actionId: "a3", actionType: "delete_vm"),
        };

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId, It.IsAny<Guid>(), "restart_vm",
                It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SafeActionProposalResponse(createdId, "None"));
        svc.Setup(s => s.ProposeAsync(
                TestTenantId, It.IsAny<Guid>(), "delete_vm",
                It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Boom"));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, _) = CreateRecorder(scopeFactory: scopeFactory);

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: proposals));

        Assert.Equal(3, result.Records.Count);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);

        var created = result.Records.Single(r => r.Status == "Created");
        Assert.Equal("p1", created.PackName);
        Assert.Equal(createdId, created.ActionRecordId);

        var skipped = result.Records.Single(r => r.Status == "Skipped");
        Assert.Equal("p2", skipped.PackName);
        Assert.Null(skipped.ActionRecordId);

        var failed = result.Records.Single(r => r.Status == "Failed");
        Assert.Equal("p3", failed.PackName);
        Assert.Null(failed.ActionRecordId);
    }

    // ── Telemetry tests ───────────────────────────────────────

    // 11. Telemetry recorded on success

    [Fact]
    public async Task RecordAsync_TelemetryRecordedOnSuccess()
    {
        var proposalItem = CreateProposal();

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId, It.IsAny<Guid>(), "restart_vm",
                It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SafeActionProposalResponse(Guid.NewGuid(), "None"));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, telemetry) = CreateRecorder(scopeFactory: scopeFactory);

        await recorder.RecordAsync(MakeRequest("C", proposals: new[] { proposalItem }));

        telemetry.Verify(
            t => t.RecordSafeActionAttempt("C", TestTenantId, TestTenantId),
            Times.Once);
        telemetry.Verify(
            t => t.RecordSafeActionCreated("azure-vm", "sa-restart", TestTenantId, TestTenantId),
            Times.Once);
    }

    // 12. Telemetry recorded on skip (non-executable)

    [Fact]
    public async Task RecordAsync_TelemetryRecordedOnSkip()
    {
        var proposal = CreateProposal(isExecutableNow: false);
        var (recorder, telemetry) = CreateRecorder();

        await recorder.RecordAsync(MakeRequest("C", proposals: new[] { proposal }));

        telemetry.Verify(
            t => t.RecordSafeActionAttempt("C", TestTenantId, TestTenantId),
            Times.Once);
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("azure-vm", "sa-restart", TestTenantId, "not_executable"),
            Times.Once);
    }

    // 13. Telemetry recorded on policy denial

    [Fact]
    public async Task RecordAsync_TelemetryRecordedOnPolicyDenied()
    {
        var proposalItem = CreateProposal();

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId, It.IsAny<Guid>(), "restart_vm",
                It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SafeActionProposalDeniedException("denied_code", "Denied!"));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, telemetry) = CreateRecorder(scopeFactory: scopeFactory);

        await recorder.RecordAsync(MakeRequest("C", proposals: new[] { proposalItem }));

        telemetry.Verify(
            t => t.RecordSafeActionDenied(
                "azure-vm", "sa-restart", TestTenantId, "denied_code", TestTenantId),
            Times.Once);
    }

    // 14. Telemetry recorded on generic failure

    [Fact]
    public async Task RecordAsync_TelemetryRecordedOnFailure()
    {
        var proposalItem = CreateProposal();

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId, It.IsAny<Guid>(), "restart_vm",
                It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Timeout"));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, telemetry) = CreateRecorder(scopeFactory: scopeFactory);

        await recorder.RecordAsync(MakeRequest("C", proposals: new[] { proposalItem }));

        telemetry.Verify(
            t => t.RecordSafeActionFailed(
                "azure-vm", "sa-restart", TestTenantId, "TimeoutException", TestTenantId),
            Times.Once);
    }

    // ── Edge-case tests ───────────────────────────────────────

    // 15. Null parametersJson defaults to "{}"

    [Fact]
    public async Task RecordAsync_NullParametersJson_DefaultsToEmptyObject()
    {
        var proposal = CreateProposal(parametersJson: null);
        var expectedId = Guid.NewGuid();

        var svc = new Mock<ISafeActionProposalService>(MockBehavior.Strict);
        svc.Setup(s => s.ProposeAsync(
                TestTenantId,
                It.IsAny<Guid>(),
                "restart_vm",
                "{}", // null parametersJson should default to "{}"
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SafeActionProposalResponse(expectedId, "None"));

        var scopeFactory = CreateScopeFactory(svc.Object);
        var (recorder, _) = CreateRecorder(scopeFactory: scopeFactory);

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: new[] { proposal }));

        var item = Assert.Single(result.Records);
        Assert.Equal("Created", item.Status);
        svc.VerifyAll();
    }

    // 16. Lowercase "c" mode also passes gate

    [Fact]
    public async Task RecordAsync_LowercaseC_PassesGate()
    {
        var config = BuildConfig(deploymentMode: "c");
        var (recorder, telemetry) = CreateRecorder(config);

        var result = await recorder.RecordAsync(
            MakeRequest("c", proposals: Array.Empty<PackSafeActionProposalItem>()));

        // Should pass both gates (IsModeC uses char.ToUpperInvariant)
        telemetry.Verify(
            t => t.RecordSafeActionAttempt("c", TestTenantId, TestTenantId),
            Times.Once);
        // Should NOT see mode_not_c skip
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("gate", "mode_not_c", It.IsAny<string>(), null),
            Times.Never);
    }

    // ── Scope tests ───────────────────────────────────────────

    // 17. Scope-denied proposal → Skipped with "scope_denied"

    [Fact]
    public async Task RecordAsync_SkipsScopeDeniedProposal()
    {
        var proposal = CreateProposal(
            scopeAllowed: false,
            scopeReasonCode: "target_scope_subscription_not_allowed",
            scopeMessage: "Subscription not in tenant allowlist.");

        var (recorder, telemetry) = CreateRecorder();

        var result = await recorder.RecordAsync(
            MakeRequest("C", proposals: new[] { proposal }));

        var item = Assert.Single(result.Records);
        Assert.Equal("Skipped", item.Status);
        Assert.Null(item.ActionRecordId);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        telemetry.Verify(
            t => t.RecordSafeActionSkipped("azure-vm", "sa-restart", TestTenantId, "scope_denied"),
            Times.Once);
    }
}
