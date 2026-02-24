using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

public class SafeActionOrchestratorTests
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
        Mock<IActionExecutor>? executor = null,
        Mock<ISafeActionPolicy>? policy = null)
    {
        executor ??= new Mock<IActionExecutor>(MockBehavior.Strict);

        if (policy is null)
        {
            policy = new Mock<ISafeActionPolicy>(MockBehavior.Strict);
            policy.Setup(p => p.Evaluate(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(PolicyDecision.Allow());
        }

        return new SafeActionOrchestrator(
            repo.Object,
            executor.Object,
            policy.Object,
            Mock.Of<ILogger<SafeActionOrchestrator>>());
    }

    // ─── ProposeAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ProposeAsync_Creates_ActionRecord_With_Proposed_Status()
    {
        var expected = CreateProposedRecord();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        repo.Setup(r => r.CreateActionRecordAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var orchestrator = CreateOrchestrator(repo);

        var result = await orchestrator.ProposeAsync(
            "t-1", Guid.NewGuid(), "restart_pod",
            "{\"target\":\"pod-1\"}", "{\"undo\":\"stop_pod\"}", null);

        Assert.Equal(ActionStatus.Proposed, result.Status);
        Assert.Equal(RollbackStatus.Available, result.RollbackStatus);

        repo.Verify(r => r.CreateActionRecordAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProposeAsync_Throws_PolicyDeniedException_When_Policy_Denies()
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        var policy = new Mock<ISafeActionPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.Evaluate("t-1", "restart_pod"))
              .Returns(PolicyDecision.Deny("BLOCKED", "Action type blocked by policy"));

        var orchestrator = CreateOrchestrator(repo, policy: policy);

        var ex = await Assert.ThrowsAsync<PolicyDeniedException>(
            () => orchestrator.ProposeAsync(
                "t-1", Guid.NewGuid(), "restart_pod",
                "{\"target\":\"pod-1\"}", null, null));

        Assert.Equal("BLOCKED", ex.ReasonCode);

        // No database row should have been created
        repo.Verify(r => r.CreateActionRecordAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProposeAsync_Succeeds_When_Policy_Allows()
    {
        var expected = CreateProposedRecord();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.CreateActionRecordAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var policy = new Mock<ISafeActionPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.Evaluate(It.IsAny<string>(), It.IsAny<string>()))
              .Returns(PolicyDecision.Allow());

        var orchestrator = CreateOrchestrator(repo, policy: policy);

        var result = await orchestrator.ProposeAsync(
            "t-1", Guid.NewGuid(), "restart_pod",
            "{\"target\":\"pod-1\"}", "{\"undo\":\"stop_pod\"}", null);

        Assert.Equal(ActionStatus.Proposed, result.Status);
        policy.Verify(p => p.Evaluate("t-1", "restart_pod"), Times.Once);
    }

    // ─── ApproveAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_Transitions_To_Approved()
    {
        var record = CreateProposedRecord();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.AppendApprovalAsync(
                It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo);
        var result = await orchestrator.ApproveAsync(
            record.ActionRecordId, "admin@ops.com", "Looks safe");

        Assert.Equal(ActionStatus.Approved, result.Status);
    }

    [Fact]
    public async Task ApproveAsync_Throws_When_Already_Approved()
    {
        var record = CreateProposedRecord();
        record.Approve(); // already approved

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var orchestrator = CreateOrchestrator(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ApproveAsync(
                record.ActionRecordId, "admin@ops.com", "duplicate"));
    }

    // ─── RejectAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_Transitions_To_Rejected()
    {
        var record = CreateProposedRecord();
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.AppendApprovalAsync(
                It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo);
        var result = await orchestrator.RejectAsync(
            record.ActionRecordId, "admin@ops.com", "Too risky");

        Assert.Equal(ActionStatus.Rejected, result.Status);
    }

    // ─── ExecuteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Completes_When_Executor_Succeeds()
    {
        var record = CreateProposedRecord();
        record.Approve();

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(
                It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionExecutionResult(true, "{\"ok\":true}", 42));

        var orchestrator = CreateOrchestrator(repo, executor);
        var result = await orchestrator.ExecuteAsync(record.ActionRecordId);

        Assert.Equal(ActionStatus.Completed, result.Status);
        Assert.NotNull(result.OutcomeJson);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Executor_Returns_Failure()
    {
        var record = CreateProposedRecord();
        record.Approve();

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(
                It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionExecutionResult(false, "{\"error\":\"timeout\"}", 5000));

        var orchestrator = CreateOrchestrator(repo, executor);
        var result = await orchestrator.ExecuteAsync(record.ActionRecordId);

        Assert.Equal(ActionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Throws_When_Not_Approved()
    {
        var record = CreateProposedRecord(); // Status = Proposed

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var orchestrator = CreateOrchestrator(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ExecuteAsync(record.ActionRecordId));
    }

    // ─── Rollback ─────────────────────────────────────────────────────

    [Fact]
    public async Task RequestRollbackAsync_Transitions_RollbackStatus_To_Pending()
    {
        var record = CreateProposedRecord();
        record.Approve();
        record.MarkExecuting();
        record.CompleteExecution("{}", "{\"ok\":true}");

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo);
        var result = await orchestrator.RequestRollbackAsync(record.ActionRecordId);

        Assert.Equal(RollbackStatus.Pending, result.RollbackStatus);
    }

    [Fact]
    public async Task ApproveRollbackAsync_Transitions_RollbackStatus_To_Approved()
    {
        var record = CreateProposedRecord();
        record.Approve();
        record.MarkExecuting();
        record.CompleteExecution("{}", "{\"ok\":true}");
        record.RequestRollback();

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.AppendApprovalAsync(
                It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo);
        var result = await orchestrator.ApproveRollbackAsync(
            record.ActionRecordId, "admin@ops.com", "Confirmed rollback");

        Assert.Equal(RollbackStatus.Approved, result.RollbackStatus);
    }

    [Fact]
    public async Task ExecuteRollbackAsync_Completes_When_Executor_Succeeds()
    {
        var record = CreateProposedRecord();
        record.Approve();
        record.MarkExecuting();
        record.CompleteExecution("{}", "{\"ok\":true}");
        record.RequestRollback();
        record.ApproveRollback();

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(
                It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.RollbackAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionExecutionResult(true, "{\"rolled_back\":true}", 10));

        var orchestrator = CreateOrchestrator(repo, executor);
        var result = await orchestrator.ExecuteRollbackAsync(record.ActionRecordId);

        Assert.Equal(RollbackStatus.RolledBack, result.RollbackStatus);
    }

    // ─── Not Found ────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_Throws_KeyNotFound_When_Record_Missing()
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActionRecord?)null);

        var orchestrator = CreateOrchestrator(repo);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => orchestrator.ApproveAsync(
                Guid.NewGuid(), "admin@ops.com", "test"));
    }

    // ─── Domain invariants ────────────────────────────────────────────

    [Fact]
    public void ActionRecord_Create_Sets_RollbackStatus_None_When_No_Rollback()
    {
        var record = ActionRecord.Create("t-1", Guid.NewGuid(), "log_query", "{}");
        Assert.Equal(RollbackStatus.None, record.RollbackStatus);
    }

    [Fact]
    public void ActionRecord_Create_Sets_RollbackStatus_Available_When_Payload_Provided()
    {
        var record = ActionRecord.Create(
            "t-1", Guid.NewGuid(), "restart_pod", "{}",
            rollbackPayloadJson: "{\"undo\":\"stop_pod\"}");
        Assert.Equal(RollbackStatus.Available, record.RollbackStatus);
    }

    [Fact]
    public void ActionRecord_Create_Sets_ManualRequired_When_Guidance_Provided()
    {
        var record = ActionRecord.Create(
            "t-1", Guid.NewGuid(), "modify_config", "{}",
            rollbackPayloadJson: null,
            manualRollbackGuidance: "Restore from backup");
        Assert.Equal(RollbackStatus.ManualRequired, record.RollbackStatus);
    }

    [Fact]
    public void ActionRecord_RequestRollback_Throws_When_Rollback_Not_Available()
    {
        var record = ActionRecord.Create("t-1", Guid.NewGuid(), "log_query", "{}");
        record.Approve();
        record.MarkExecuting();
        record.CompleteExecution("{}", "{}");

        // RollbackStatus is None
        Assert.Throws<InvalidOperationException>(() => record.RequestRollback());
    }

    [Fact]
    public void ActionRecord_MarkExecuting_Throws_When_Not_Approved()
    {
        var record = ActionRecord.Create("t-1", Guid.NewGuid(), "restart_pod", "{}");
        Assert.Throws<InvalidOperationException>(() => record.MarkExecuting());
    }
}
