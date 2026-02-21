using OpsCopilot.AgentRuns.Presentation.Contracts;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Tests for <see cref="TriageRequest"/> — specifically the optional
/// <c>WorkspaceId</c> parameter added for the WORKSPACE_ID dependency fix.
/// </summary>
public sealed class TriageRequestTests
{
    private static readonly AlertPayloadDto ValidPayload = new(
        AlertSource: "AzureMonitor",
        Fingerprint: "abc-123");

    // ── WorkspaceId defaults ──────────────────────────────────────────────

    [Fact]
    public void WorkspaceId_DefaultsToNull()
    {
        var request = new TriageRequest(ValidPayload);

        Assert.Null(request.WorkspaceId);
    }

    [Fact]
    public void TimeRangeMinutes_DefaultsTo120()
    {
        var request = new TriageRequest(ValidPayload);

        Assert.Equal(120, request.TimeRangeMinutes);
    }

    // ── WorkspaceId can be set ────────────────────────────────────────────

    [Fact]
    public void WorkspaceId_CanBeSet()
    {
        var request = new TriageRequest(ValidPayload, WorkspaceId: "ws-override");

        Assert.Equal("ws-override", request.WorkspaceId);
    }

    [Fact]
    public void WorkspaceId_CanBeSetWithNamedParameter()
    {
        var request = new TriageRequest(
            AlertPayload: ValidPayload,
            TimeRangeMinutes: 60,
            WorkspaceId: "00000000-0000-0000-0000-000000000001");

        Assert.Equal("00000000-0000-0000-0000-000000000001", request.WorkspaceId);
        Assert.Equal(60, request.TimeRangeMinutes);
    }

    [Fact]
    public void WorkspaceId_CanBeExplicitlyNull()
    {
        var request = new TriageRequest(ValidPayload, WorkspaceId: null);

        Assert.Null(request.WorkspaceId);
    }

    // ── Record equality ───────────────────────────────────────────────────

    [Fact]
    public void TwoRequests_WithSameWorkspaceId_AreEqual()
    {
        var r1 = new TriageRequest(ValidPayload, WorkspaceId: "ws-1");
        var r2 = new TriageRequest(ValidPayload, WorkspaceId: "ws-1");

        Assert.Equal(r1, r2);
    }

    [Fact]
    public void TwoRequests_WithDifferentWorkspaceId_AreNotEqual()
    {
        var r1 = new TriageRequest(ValidPayload, WorkspaceId: "ws-1");
        var r2 = new TriageRequest(ValidPayload, WorkspaceId: "ws-2");

        Assert.NotEqual(r1, r2);
    }
}
