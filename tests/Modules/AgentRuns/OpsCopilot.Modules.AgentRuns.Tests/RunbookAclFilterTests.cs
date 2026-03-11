using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Acl;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.BuildingBlocks.Contracts.Rag;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class RunbookAclFilterTests
{
    private const string TenantId          = "tenant-001";
    private const string AlertFingerprint  = "AABBCCDD";
    private const string WorkspaceId       = "ws-123";
    private const int    Minutes           = 30;

    // ─── Pure unit tests for PermissiveRunbookAclFilter ────────────────────

    [Fact]
    public void PermissiveFilter_ReturnsAllHits_Unchanged()
    {
        var hits = new List<RunbookSearchHit>
        {
            new("rb-1", "Title One", "Snippet one", 0.9),
            new("rb-2", "Title Two", "Snippet two", 0.7),
        };
        var caller = RunbookCallerContext.TenantOnly("tenant-001");
        var sut = new PermissiveRunbookAclFilter();

        var result = sut.Filter(hits, caller);

        Assert.Equal(2, result.Count);
        Assert.Same(hits[0], result[0]);
        Assert.Same(hits[1], result[1]);
    }

    [Fact]
    public void PermissiveFilter_EmptyList_ReturnsEmpty()
    {
        var sut    = new PermissiveRunbookAclFilter();
        var result = sut.Filter([], RunbookCallerContext.TenantOnly("t"));
        Assert.Empty(result);
    }

    // ─── Integration tests: ACL filter wired into TriageOrchestrator ───────

    [Fact]
    public async Task Orchestrator_AclFilter_IsCalledWithCorrectContext()
    {
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded)   = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy)   = CreateDefaultSessionMocks();

        var filterMock = new Mock<IRunbookAclFilter>(MockBehavior.Strict);
        filterMock
            .Setup(f => f.Filter(
                It.IsAny<IReadOnlyList<RunbookSearchHit>>(),
                It.IsAny<RunbookCallerContext>()))
            .Returns((IReadOnlyList<RunbookSearchHit> hits, RunbookCallerContext _) => hits);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, filterMock.Object);

        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Single(result.RunbookCitations);
        Assert.Equal("high-cpu", result.RunbookCitations[0].RunbookId);
        filterMock.Verify(
            f => f.Filter(
                It.IsAny<IReadOnlyList<RunbookSearchHit>>(),
                It.Is<RunbookCallerContext>(c => c.TenantId == TenantId)),
            Times.Once);
    }

    [Fact]
    public async Task Orchestrator_WhenFilterReturnsEmpty_RunbookCitationsIsEmpty()
    {
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded)   = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy)   = CreateDefaultSessionMocks();

        var filterMock = new Mock<IRunbookAclFilter>(MockBehavior.Strict);
        filterMock
            .Setup(f => f.Filter(
                It.IsAny<IReadOnlyList<RunbookSearchHit>>(),
                It.IsAny<RunbookCallerContext>()))
            .Returns(Array.Empty<RunbookSearchHit>());

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, filterMock.Object);

        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Empty(result.RunbookCitations);
        Assert.NotEmpty(result.Citations);
    }

    [Fact]
    public async Task Orchestrator_WhenFilterReturnsSubset_OnlyAuthorizedHitsBecomeCitations()
    {
        var hit1 = new RunbookSearchHit("rb-allowed", "Allowed Runbook", "Snippet A", 0.9);
        var hit2 = new RunbookSearchHit("rb-denied",  "Denied Runbook",  "Snippet B", 0.8);

        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var (allowlist, budget, degraded)   = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy)   = CreateDefaultSessionMocks();

        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        runbookMock
            .Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunbookSearchToolResponse(
                Ok:    true,
                Hits:  new List<RunbookSearchHit> { hit1, hit2 },
                Query: AlertFingerprint));

        var filterMock = new Mock<IRunbookAclFilter>(MockBehavior.Strict);
        filterMock
            .Setup(f => f.Filter(
                It.IsAny<IReadOnlyList<RunbookSearchHit>>(),
                It.IsAny<RunbookCallerContext>()))
            .Returns(new List<RunbookSearchHit> { hit1 });

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, filterMock.Object);

        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Single(result.RunbookCitations);
        Assert.Equal("rb-allowed", result.RunbookCitations[0].RunbookId);
    }

    // ─── Unit tests for TenantGroupRoleRunbookAclFilter ────────────────────

    [Fact]
    public void TenantGroupRoleFilter_NullGroupsNullRoles_HitIsAllowed()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-1", "Open Runbook", "No restrictions", 0.9);
        var caller = RunbookCallerContext.TenantOnly(TenantId);

        var result = filter.Filter([hit], caller);

        Assert.Single(result);
        Assert.Equal("rb-1", result[0].RunbookId);
    }

    [Fact]
    public void TenantGroupRoleFilter_GroupsRestricted_CallerHasNoGroups_IsDenied()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-2", "SRE Only", "Restricted", 0.9,
                         AllowedGroups: ["sre"]);
        var caller = RunbookCallerContext.TenantOnly(TenantId); // Groups = []

        var result = filter.Filter([hit], caller);

        Assert.Empty(result);
    }

    [Fact]
    public void TenantGroupRoleFilter_RolesRestricted_CallerHasNoRoles_IsDenied()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-3", "Admin Only", "Restricted", 0.9,
                         AllowedRoles: ["admin"]);
        var caller = RunbookCallerContext.TenantOnly(TenantId); // Roles = []

        var result = filter.Filter([hit], caller);

        Assert.Empty(result);
    }

    [Fact]
    public void TenantGroupRoleFilter_GroupsRestricted_CallerGroupMatches_IsAllowed()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-4", "SRE Runbook", "Group restricted", 0.9,
                         AllowedGroups: ["sre", "ops"]);
        var caller = new RunbookCallerContext(TenantId, ["sre"], []);

        var result = filter.Filter([hit], caller);

        Assert.Single(result);
        Assert.Equal("rb-4", result[0].RunbookId);
    }

    [Fact]
    public void TenantGroupRoleFilter_RolesRestricted_CallerRoleMatches_IsAllowed()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-5", "Operator Runbook", "Role restricted", 0.9,
                         AllowedRoles: ["operator", "admin"]);
        var caller = new RunbookCallerContext(TenantId, [], ["admin"]);

        var result = filter.Filter([hit], caller);

        Assert.Single(result);
        Assert.Equal("rb-5", result[0].RunbookId);
    }

    [Fact]
    public void TenantGroupRoleFilter_GroupsRestricted_CallerGroupNoMatch_IsDenied()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-6", "Restricted Runbook", "No match", 0.9,
                         AllowedGroups: ["sre"]);
        var caller = new RunbookCallerContext(TenantId, ["dev", "qa"], []);

        var result = filter.Filter([hit], caller);

        Assert.Empty(result);
    }

    [Fact]
    public void TenantGroupRoleFilter_MixedHits_ReturnsOnlyAuthorized()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hits = new List<RunbookSearchHit>
        {
            new("rb-open",   "Open",        "No ACL",        0.9),                          // allowed
            new("rb-group",  "Group SRE",   "Group ACL",     0.8, AllowedGroups: ["sre"]),  // allowed
            new("rb-denied", "Admin Only",  "Role denied",   0.7, AllowedRoles:  ["admin"]), // denied
            new("rb-both",   "SRE+Ops",     "Group+Role",    0.6,
                    AllowedGroups: ["ops"], AllowedRoles: ["admin"]),                        // denied
        };
        var caller = new RunbookCallerContext(TenantId, ["sre"], []);

        var result = filter.Filter(hits, caller);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.RunbookId == "rb-open");
        Assert.Contains(result, r => r.RunbookId == "rb-group");
    }

    [Fact]
    public void TenantGroupRoleFilter_EmptyInput_ReturnsEmpty()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var caller = RunbookCallerContext.TenantOnly(TenantId);

        var result = filter.Filter([], caller);

        Assert.Empty(result);
    }

    [Fact]
    public void TenantGroupRoleFilter_AllDenied_ReturnsEmpty()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hits = new List<RunbookSearchHit>
        {
            new("rb-1", "T1", "S1", 0.9, AllowedGroups: ["sre"]),
            new("rb-2", "T2", "S2", 0.8, AllowedRoles:  ["admin"]),
        };
        var caller = RunbookCallerContext.TenantOnly(TenantId); // no groups, no roles

        var result = filter.Filter(hits, caller);

        Assert.Empty(result);
    }

    [Fact]
    public void TenantGroupRoleFilter_MultipleGroups_FirstMatches_IsAllowed()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-1", "Multi Group", "Snippet", 0.9,
                         AllowedGroups: ["sre", "ops", "dev"]);
        var caller = new RunbookCallerContext(TenantId, ["sre", "qa"], []);

        var result = filter.Filter([hit], caller);

        Assert.Single(result);
    }

    [Fact]
    public void TenantGroupRoleFilter_BothGroupsAndRoles_OnlyRoleMatches_IsAllowed()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-1", "OR Logic Role", "Snippet", 0.9,
                         AllowedGroups: ["sre"],
                         AllowedRoles:  ["admin"]);
        // caller has no matching group but has the role
        var caller = new RunbookCallerContext(TenantId, ["dev"], ["admin"]);

        var result = filter.Filter([hit], caller);

        Assert.Single(result);
        Assert.Equal("rb-1", result[0].RunbookId);
    }

    [Fact]
    public void TenantGroupRoleFilter_BothGroupsAndRoles_OnlyGroupMatches_IsAllowed()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-1", "OR Logic Group", "Snippet", 0.9,
                         AllowedGroups: ["sre"],
                         AllowedRoles:  ["admin"]);
        // caller has the group but no matching role
        var caller = new RunbookCallerContext(TenantId, ["sre"], ["viewer"]);

        var result = filter.Filter([hit], caller);

        Assert.Single(result);
        Assert.Equal("rb-1", result[0].RunbookId);
    }

    [Fact]
    public void TenantGroupRoleFilter_EmptyAllowedGroupsList_NonNullEmptyList_IsDenied()
    {
        // AllowedGroups = [] (non-null but empty) means restricted to nobody → deny
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-1", "Nobody Allowed", "Snippet", 0.9,
                         AllowedGroups: []);
        var caller = new RunbookCallerContext(TenantId, ["sre", "admin"], ["operator"]);

        var result = filter.Filter([hit], caller);

        Assert.Empty(result);
    }

    [Fact]
    public void TenantGroupRoleFilter_GroupMatch_IsCaseInsensitive()
    {
        var filter = new TenantGroupRoleRunbookAclFilter();
        var hit    = new RunbookSearchHit("rb-1", "SRE Runbook", "Snippet", 0.9,
                         AllowedGroups: ["SRE"]);
        var caller = new RunbookCallerContext(TenantId, ["sre"], []); // lowercase caller group

        var result = filter.Filter([hit], caller);

        Assert.Single(result);
        Assert.Equal("rb-1", result[0].RunbookId);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static Mock<IAgentRunRepository> CreateHappyPathRepo(AgentRun agentRun)
    {
        var mock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        mock.Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        mock.Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Completed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<IKqlToolClient> CreateHappyPathKql()
    {
        var mock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        mock.Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KqlToolResponse(
                Ok: true,
                Rows: new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["message"] = "test log line" }
                },
                ExecutedQuery: "search * | where TimeGenerated > ago(30m) | take 20",
                WorkspaceId: WorkspaceId,
                Timespan: "PT30M",
                ExecutedAtUtc: DateTimeOffset.UtcNow,
                Error: null));
        return mock;
    }

    private static Mock<IRunbookSearchToolClient> CreateHappyPathRunbook()
    {
        var mock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        mock.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunbookSearchToolResponse(
                Ok:   true,
                Hits: new List<RunbookSearchHit>
                {
                    new("high-cpu", "High CPU Troubleshooting", "Check top processes...", 0.85)
                },
                Query: AlertFingerprint));
        return mock;
    }

    private static (Mock<IToolAllowlistPolicy>, Mock<ITokenBudgetPolicy>, Mock<IDegradedModePolicy>)
        CreateAllowAllGovernanceMocks()
    {
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(PolicyDecision.Allow());
        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow());
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded.Setup(d => d.MapFailure(It.IsAny<Exception>()))
                .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "Unexpected error", false));
        return (allowlist, budget, degraded);
    }

    private static (Mock<ISessionStore>, Mock<ISessionPolicy>) CreateDefaultSessionMocks()
    {
        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy.Setup(p => p.GetSessionTtl(It.IsAny<string>()))
                     .Returns(TimeSpan.FromMinutes(30));
        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, TimeSpan ttl, CancellationToken _) =>
                new SessionInfo(Guid.NewGuid(), tenantId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(ttl), true));
        return (sessionStore, sessionPolicy);
    }
}
