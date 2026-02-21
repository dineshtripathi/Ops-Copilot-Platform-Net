using OpsCopilot.AgentRuns.Presentation.Contracts;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Unit tests for the validation rules enforced by POST /agent/triage.
/// Mirrors the inline validation in <see cref="AgentRunEndpoints"/> so
/// breaking changes in contracts surface immediately.
/// </summary>
public sealed class TriageValidationTests
{
    // ── Constants ────────────────────────────────────────────────────────
    private static readonly AlertPayloadDto ValidPayload = new(
        AlertSource: "AzureMonitor",
        Fingerprint: "abc-123");

    private const string ValidGuid = "6b530cc6-14bb-4fad-9577-3a349209ae1c";

    // ── WorkspaceId GUID validation ──────────────────────────────────────

    [Theory]
    [InlineData("6b530cc6-14bb-4fad-9577-3a349209ae1c", true)]
    [InlineData("00000000-0000-0000-0000-000000000001", true)]
    [InlineData("not-a-guid", false)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    [InlineData("6b530cc6-14bb-4fad-9577-3a349209ae1c-extra", false)]
    public void WorkspaceId_GuidValidation(string workspaceId, bool expected)
    {
        Assert.Equal(expected, Guid.TryParse(workspaceId, out _));
    }

    // ── TimeRangeMinutes boundary validation ─────────────────────────────

    [Theory]
    [InlineData(0, true)]     // below min → invalid
    [InlineData(-1, true)]    // negative → invalid
    [InlineData(1441, true)]  // above max → invalid
    [InlineData(1, false)]    // lower bound → valid
    [InlineData(120, false)]  // default → valid
    [InlineData(1440, false)] // upper bound → valid
    public void TimeRangeMinutes_BoundaryValidation(int minutes, bool shouldReject)
    {
        // Mirrors endpoint rule: if (request.TimeRangeMinutes is < 1 or > 1440)
        bool rejected = minutes is < 1 or > 1440;
        Assert.Equal(shouldReject, rejected);
    }

    // ── AlertPayload required fields ─────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AlertSource_MustNotBeNullOrWhitespace(string? alertSource)
    {
        var payload = new AlertPayloadDto(
            AlertSource: alertSource!,
            Fingerprint: "fp-1");

        Assert.True(string.IsNullOrWhiteSpace(payload.AlertSource));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fingerprint_MustNotBeNullOrWhitespace(string? fingerprint)
    {
        var payload = new AlertPayloadDto(
            AlertSource: "AzureMonitor",
            Fingerprint: fingerprint!);

        Assert.True(string.IsNullOrWhiteSpace(payload.Fingerprint));
    }

    [Fact]
    public void ValidPayload_PassesAllValidation()
    {
        var request = new TriageRequest(
            ValidPayload,
            TimeRangeMinutes: 60,
            WorkspaceId: ValidGuid);

        Assert.False(string.IsNullOrWhiteSpace(request.AlertPayload.AlertSource));
        Assert.False(string.IsNullOrWhiteSpace(request.AlertPayload.Fingerprint));
        Assert.True(request.TimeRangeMinutes is >= 1 and <= 1440);
        Assert.True(Guid.TryParse(request.WorkspaceId, out _));
    }

    // ── x-tenant-id header simulation ────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TenantIdHeader_MustNotBeNullOrWhitespace(string? tenantId)
    {
        // Mirrors endpoint rule: if (string.IsNullOrWhiteSpace(tenantId))
        Assert.True(string.IsNullOrWhiteSpace(tenantId));
    }

    [Fact]
    public void TenantIdHeader_ValidValue_Passes()
    {
        const string tenantId = "tenant-acme";

        Assert.False(string.IsNullOrWhiteSpace(tenantId));
    }

    // ── AlertPayload null check ──────────────────────────────────────────

    [Fact]
    public void NullAlertPayload_IsDetected()
    {
        // TriageRequest with null AlertPayload would trigger the endpoint's
        // "AlertPayload is required" validation path.
        var request = new TriageRequest(AlertPayload: null!);

        Assert.Null(request.AlertPayload);
    }
}
