using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Verifies that orchestrator methods emit the correct telemetry counters
/// via <see cref="ISafeActionsTelemetry"/>.
/// Slice 18 — SafeActions Execution Observability.
/// </summary>
public class SafeActionsTelemetryTests
{
    // ─── Helpers ──────────────────────────────────────────────────────

    private static ActionRecord CreateProposedRecord(
        string tenantId = "t-1",
        Guid? runId = null,
        string actionType = "restart_pod",
        string? rollbackPayloadJson = "{\"undo\":\"stop_pod\"}")
    {
        return ActionRecord.Create(
            tenantId,
            runId ?? Guid.NewGuid(),
            actionType,
            "{\"target\":\"pod-1\"}",
            rollbackPayloadJson);
    }

    private static SafeActionOrchestrator CreateOrchestrator(
        Mock<IActionRecordRepository> repo,
        Mock<ISafeActionsTelemetry> telemetry,
        Mock<IActionExecutor>? executor = null,
        Mock<ISafeActionPolicy>? policy = null,
        Mock<ITenantExecutionPolicy>? tenantPolicy = null)
    {
        executor ??= new Mock<IActionExecutor>(MockBehavior.Strict);

        if (policy is null)
        {
            policy = new Mock<ISafeActionPolicy>(MockBehavior.Strict);
            policy.Setup(p => p.Evaluate(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(PolicyDecision.Allow());
        }

        if (tenantPolicy is null)
        {
            tenantPolicy = new Mock<ITenantExecutionPolicy>(MockBehavior.Strict);
            tenantPolicy.Setup(p => p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()))
                        .Returns(PolicyDecision.Allow());
        }

        return new SafeActionOrchestrator(
            repo.Object,
            executor.Object,
            policy.Object,
            tenantPolicy.Object,
            telemetry.Object,
            Mock.Of<ILogger<SafeActionOrchestrator>>());
    }

    // ─── ProposeAsync — Policy Denied ─────────────────────────────

    [Fact]
    public async Task ProposeAsync_PolicyDenied_RecordsPolicyDenied()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        var policy = new Mock<ISafeActionPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.Evaluate("t-1", "restart_pod"))
              .Returns(PolicyDecision.Deny("BLOCKED", "blocked by policy"));

        var orchestrator = CreateOrchestrator(repo, telemetry, policy: policy);

        await Assert.ThrowsAsync<PolicyDeniedException>(
            () => orchestrator.ProposeAsync(
                "t-1", Guid.NewGuid(), "restart_pod",
                "{}", null, null));

        telemetry.Verify(t => t.RecordPolicyDenied("restart_pod", "t-1"), Times.Once);
    }

    // ─── ExecuteAsync — Success path ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Success_RecordsAttemptAndSuccess()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);

        var record = CreateProposedRecord();
        record.Approve();

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        executor.Setup(e => e.ExecuteAsync("restart_pod", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActionExecutionResult(true, "{\"ok\":true}", 42));

        var orchestrator = CreateOrchestrator(repo, telemetry, executor: executor);

        await orchestrator.ExecuteAsync(record.ActionRecordId);

        telemetry.Verify(t => t.RecordExecutionAttempt("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionSuccess("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionFailure(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ─── ExecuteAsync — Failure path ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Failure_RecordsAttemptAndFailure()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);

        var record = CreateProposedRecord();
        record.Approve();

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        executor.Setup(e => e.ExecuteAsync("restart_pod", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActionExecutionResult(false, "{\"error\":\"timeout\"}", 100));

        var orchestrator = CreateOrchestrator(repo, telemetry, executor: executor);

        await orchestrator.ExecuteAsync(record.ActionRecordId);

        telemetry.Verify(t => t.RecordExecutionAttempt("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionFailure("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionSuccess(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ─── ExecuteAsync — Tenant policy denied ──────────────────────

    [Fact]
    public async Task ExecuteAsync_TenantPolicyDenied_RecordsAttemptAndPolicyDenied()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        var record = CreateProposedRecord();
        record.Approve();

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var tenantPolicy = new Mock<ITenantExecutionPolicy>(MockBehavior.Strict);
        tenantPolicy.Setup(p => p.EvaluateExecution("t-1", "restart_pod"))
                    .Returns(PolicyDecision.Deny("TENANT_BLOCKED", "blocked"));

        var orchestrator = CreateOrchestrator(repo, telemetry, tenantPolicy: tenantPolicy);

        await Assert.ThrowsAsync<PolicyDeniedException>(
            () => orchestrator.ExecuteAsync(record.ActionRecordId));

        telemetry.Verify(t => t.RecordExecutionAttempt("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordPolicyDenied("restart_pod", "t-1"), Times.Once);
    }

    // ─── ExecuteAsync — Replay conflict ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_ReplayConflict_RecordsAttemptAndReplayConflict()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        // Record is still Proposed (not Approved), so replay guard triggers
        var record = CreateProposedRecord();

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var orchestrator = CreateOrchestrator(repo, telemetry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ExecuteAsync(record.ActionRecordId));

        telemetry.Verify(t => t.RecordExecutionAttempt("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordReplayConflict("restart_pod"), Times.Once);
    }

    // ─── ExecuteRollbackAsync — Success path ──────────────────────

    [Fact]
    public async Task ExecuteRollbackAsync_Success_RecordsAttemptAndSuccess()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);

        var record = CreateProposedRecord();
        record.Approve();
        // Advance to Completed via state machine
        record.MarkExecuting();
        record.CompleteExecution("{}", "{\"ok\":true}");
        record.RequestRollback();
        record.ApproveRollback();

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        executor.Setup(e => e.RollbackAsync("restart_pod", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActionExecutionResult(true, "{\"rolled_back\":true}", 30));

        var orchestrator = CreateOrchestrator(repo, telemetry, executor: executor);

        await orchestrator.ExecuteRollbackAsync(record.ActionRecordId);

        telemetry.Verify(t => t.RecordExecutionAttempt("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionSuccess("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionFailure(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ─── ExecuteRollbackAsync — Failure path ──────────────────────

    [Fact]
    public async Task ExecuteRollbackAsync_Failure_RecordsAttemptAndFailure()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);

        var record = CreateProposedRecord();
        record.Approve();
        record.MarkExecuting();
        record.CompleteExecution("{}", "{\"ok\":true}");
        record.RequestRollback();
        record.ApproveRollback();

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        executor.Setup(e => e.RollbackAsync("restart_pod", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActionExecutionResult(false, "{\"error\":\"fail\"}", 50));

        var orchestrator = CreateOrchestrator(repo, telemetry, executor: executor);

        await orchestrator.ExecuteRollbackAsync(record.ActionRecordId);

        telemetry.Verify(t => t.RecordExecutionAttempt("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionFailure("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordExecutionSuccess(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ─── ExecuteRollbackAsync — Tenant policy denied ──────────────

    [Fact]
    public async Task ExecuteRollbackAsync_TenantPolicyDenied_RecordsAttemptAndPolicyDenied()
    {
        var telemetry = new Mock<ISafeActionsTelemetry>();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        var record = CreateProposedRecord();
        record.Approve();
        record.MarkExecuting();
        record.CompleteExecution("{}", "{\"ok\":true}");
        record.RequestRollback();
        record.ApproveRollback();

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var tenantPolicy = new Mock<ITenantExecutionPolicy>(MockBehavior.Strict);
        tenantPolicy.Setup(p => p.EvaluateExecution("t-1", "restart_pod"))
                    .Returns(PolicyDecision.Deny("BLOCKED", "blocked"));

        var orchestrator = CreateOrchestrator(repo, telemetry, tenantPolicy: tenantPolicy);

        await Assert.ThrowsAsync<PolicyDeniedException>(
            () => orchestrator.ExecuteRollbackAsync(record.ActionRecordId));

        telemetry.Verify(t => t.RecordExecutionAttempt("restart_pod", "t-1"), Times.Once);
        telemetry.Verify(t => t.RecordPolicyDenied("restart_pod", "t-1"), Times.Once);
    }
}
