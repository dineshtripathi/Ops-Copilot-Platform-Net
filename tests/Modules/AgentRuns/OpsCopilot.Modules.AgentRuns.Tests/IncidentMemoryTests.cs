using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Acl;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Application.Services;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Infrastructure.Memory;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Rag.Application.Memory;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class IncidentMemoryTests
{
    // ── NullIncidentMemoryService ───────────────────────────────────────────

    [Fact]
    public async Task NullService_RecallAsync_ReturnsEmpty()
    {
        var sut = new NullIncidentMemoryService();
        var result = await sut.RecallAsync("fp", "tenant1");
        Assert.Empty(result);
    }

    // ── RagBackedIncidentMemoryService ──────────────────────────────────────

    [Fact]
    public async Task RagBackedService_RecallAsync_MapsHitsToCitations()
    {
        var hit = new IncidentMemoryHit(
            "run-1", "tenant1", "fp-1", "summary text", 0.9, DateTimeOffset.UtcNow);

        var retrieval = new Mock<IIncidentMemoryRetrievalService>(MockBehavior.Strict);
        retrieval
            .Setup(r => r.SearchAsync(It.IsAny<IncidentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { hit });

        var sut = new RagBackedIncidentMemoryService(retrieval.Object);

        var citations = await sut.RecallAsync("fp-1", "tenant1");

        var c = Assert.Single(citations);
        Assert.Equal("run-1", c.RunId);
        Assert.Equal("fp-1", c.AlertFingerprint);
        Assert.Equal("summary text", c.SummarySnippet);
        Assert.Equal(0.9, c.Score);
    }

    [Fact]
    public async Task RagBackedService_RecallAsync_ReturnsEmpty_WhenNoHits()
    {
        var retrieval = new Mock<IIncidentMemoryRetrievalService>(MockBehavior.Strict);
        retrieval
            .Setup(r => r.SearchAsync(It.IsAny<IncidentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IncidentMemoryHit>());

        var sut = new RagBackedIncidentMemoryService(retrieval.Object);

        var result = await sut.RecallAsync("fp", "tenant1");

        Assert.Empty(result);
    }

    // ── TriageOrchestrator accepts null memory (smoke) ─────────────────────

    [Fact]
    public void TriageOrchestrator_AcceptsNullMemory_DoesNotThrow()
    {
        var repo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        var kql  = new Mock<IKqlToolClient>(MockBehavior.Strict);
        var rb   = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(PolicyDecision.Allow());

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow());

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded.Setup(d => d.MapFailure(It.IsAny<Exception>()))
                .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "Unexpected error", false));

        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy.Setup(p => p.GetSessionTtl(It.IsAny<string>()))
                     .Returns(TimeSpan.FromMinutes(30));

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.CreateAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, TimeSpan ttl, CancellationToken _) =>
                new SessionInfo(Guid.NewGuid(), tenantId, DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.Add(ttl), true));

        // memory param defaults to null — verifies constructor compiles and runs
        var sut = new TriageOrchestrator(
            repo.Object, kql.Object, rb.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System,
            new PermissiveRunbookAclFilter());

        Assert.NotNull(sut);
    }
}
