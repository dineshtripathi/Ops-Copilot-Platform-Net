using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Slice 173 — Citation Integrity Validator unit tests.
///
/// Covers DefaultCitationIntegrityValidator (all four citation types),
/// NullCitationValidator, and CitationValidationResult factory methods.
/// No IO, no mocks — pure unit tests on the validator directly.
/// </summary>
public sealed class CitationValidatorSlice173Tests
{
    private static readonly DefaultCitationIntegrityValidator Sut = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static KqlCitation ValidKql()
        => new("ws-1", "AzureActivity | limit 10", "P1D", DateTimeOffset.UtcNow);

    private static RunbookCitation ValidRunbook()
        => new("rb-1", "DB Failover Guide", "Check replica health.", 0.9);

    private static MemoryCitation ValidMemory()
        => new("run-1", "FP-173", "High CPU on node.", 0.8, DateTimeOffset.UtcNow);

    private static DeploymentDiffCitation ValidDiff()
        => new("sub-1", "rg-ops", "res-1", "Update", DateTimeOffset.UtcNow, "Config changed.");

    // ── CitationValidationResult factory ─────────────────────────────────────

    [Fact]
    public void CitationValidationResult_Pass_IsValidTrueAndNoViolations()
    {
        var result = CitationValidationResult.Pass();

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CitationValidationResult_Fail_IsValidFalseWithMessages()
    {
        var violations = new[] { "Field A is empty.", "Field B is missing." };
        var result = CitationValidationResult.Fail(violations);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Violations.Count);
    }

    // ── Empty lists ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllEmptyLists_Passes()
    {
        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    // ── KqlCitation ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidKqlCitation_Passes()
    {
        var result = Sut.Validate(
            new[] { ValidKql() },
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_KqlCitation_EmptyWorkspaceId_Fails()
    {
        var kql = new KqlCitation("", "AzureActivity | limit 10", "P1D", DateTimeOffset.UtcNow);

        var result = Sut.Validate(
            new[] { kql },
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("WorkspaceId"));
    }

    [Fact]
    public void Validate_KqlCitation_WhitespaceWorkspaceId_Fails()
    {
        var kql = new KqlCitation("   ", "AzureActivity | limit 10", "P1D", DateTimeOffset.UtcNow);

        var result = Sut.Validate(
            new[] { kql },
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("WorkspaceId"));
    }

    [Fact]
    public void Validate_KqlCitation_EmptyExecutedQuery_Fails()
    {
        var kql = new KqlCitation("ws-1", "", "P1D", DateTimeOffset.UtcNow);

        var result = Sut.Validate(
            new[] { kql },
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("ExecutedQuery"));
    }

    // ── RunbookCitation ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidRunbookCitation_Passes()
    {
        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            new[] { ValidRunbook() },
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RunbookCitation_EmptyRunbookId_Fails()
    {
        var rb = new RunbookCitation("", "Title", "Snippet.", 0.5);

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            new[] { rb },
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("RunbookId"));
    }

    [Fact]
    public void Validate_RunbookCitation_EmptyTitle_Fails()
    {
        var rb = new RunbookCitation("rb-1", "", "Snippet.", 0.5);

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            new[] { rb },
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("Title"));
    }

    [Fact]
    public void Validate_RunbookCitation_NegativeScore_Fails()
    {
        var rb = new RunbookCitation("rb-1", "Title", "Snippet.", -0.1);

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            new[] { rb },
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("Score") && v.Contains("negative"));
    }

    [Fact]
    public void Validate_RunbookCitation_ZeroScore_Passes()
    {
        var rb = new RunbookCitation("rb-1", "Title", "Snippet.", 0.0);

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            new[] { rb },
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.True(result.IsValid);
    }

    // ── MemoryCitation ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidMemoryCitation_Passes()
    {
        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            new[] { ValidMemory() },
            Array.Empty<DeploymentDiffCitation>());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MemoryCitation_EmptyRunId_Fails()
    {
        var m = new MemoryCitation("", "FP-173", "Snippet.", 0.7, DateTimeOffset.UtcNow);

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            new[] { m },
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("RunId"));
    }

    [Fact]
    public void Validate_MemoryCitation_EmptyAlertFingerprint_Fails()
    {
        var m = new MemoryCitation("run-1", "", "Snippet.", 0.7, DateTimeOffset.UtcNow);

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            new[] { m },
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("AlertFingerprint"));
    }

    [Fact]
    public void Validate_MemoryCitation_NegativeScore_Fails()
    {
        var m = new MemoryCitation("run-1", "FP-173", "Snippet.", -1.0, DateTimeOffset.UtcNow);

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            new[] { m },
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("Score") && v.Contains("negative"));
    }

    // ── DeploymentDiffCitation ────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidDeploymentDiffCitation_Passes()
    {
        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            new[] { ValidDiff() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DeploymentDiff_EmptySubscriptionId_Fails()
    {
        var d = new DeploymentDiffCitation("", "rg-ops", "res-1", "Update", DateTimeOffset.UtcNow, "Summary.");

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            new[] { d });

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("SubscriptionId"));
    }

    [Fact]
    public void Validate_DeploymentDiff_EmptyResourceId_Fails()
    {
        var d = new DeploymentDiffCitation("sub-1", "rg-ops", "", "Update", DateTimeOffset.UtcNow, "Summary.");

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            new[] { d });

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("ResourceId"));
    }

    [Fact]
    public void Validate_DeploymentDiff_EmptyChangeType_Fails()
    {
        var d = new DeploymentDiffCitation("sub-1", "rg-ops", "res-1", "", DateTimeOffset.UtcNow, "Summary.");

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            new[] { d });

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("ChangeType"));
    }

    [Fact]
    public void Validate_DeploymentDiff_DefaultChangeTime_Fails()
    {
        var d = new DeploymentDiffCitation("sub-1", "rg-ops", "res-1", "Update", default, "Summary.");

        var result = Sut.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            new[] { d });

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("ChangeTime"));
    }

    // ── Multi-violation accumulation ─────────────────────────────────────────

    [Fact]
    public void Validate_MultipleViolations_AllReported()
    {
        // Two invalid KQL citations → 4 violations total
        var kql1 = new KqlCitation("", "", "P1D", DateTimeOffset.UtcNow);
        var kql2 = new KqlCitation("", "SELECT 1", "P1D", DateTimeOffset.UtcNow);

        var result = Sut.Validate(
            new[] { kql1, kql2 },
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        Assert.True(result.Violations.Count >= 3,
            $"Expected ≥3 violations, got {result.Violations.Count}: {string.Join(", ", result.Violations)}");
    }

    [Fact]
    public void Validate_MixedCitationTypes_AccumulatesAllViolations()
    {
        var badKql     = new KqlCitation("", "", "P1D", DateTimeOffset.UtcNow);
        var badRunbook = new RunbookCitation("", "", "Snippet.", -1.0);

        var result = Sut.Validate(
            new[] { badKql },
            new[] { badRunbook },
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.False(result.IsValid);
        // kql: 2 violations + runbook: 3 violations = 5 total
        Assert.True(result.Violations.Count >= 4,
            $"Expected ≥4 violations, got {result.Violations.Count}: {string.Join(", ", result.Violations)}");
    }

    // ── NullCitationValidator ─────────────────────────────────────────────────

    [Fact]
    public void NullCitationValidator_AllEmptyLists_AlwaysPasses()
    {
        var validator = new NullCitationValidator();

        var result = validator.Validate(
            Array.Empty<KqlCitation>(),
            Array.Empty<RunbookCitation>(),
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void NullCitationValidator_InvalidCitations_StillPasses()
    {
        var validator  = new NullCitationValidator();
        var badKql     = new KqlCitation("", "", "P1D", DateTimeOffset.UtcNow);
        var badRunbook = new RunbookCitation("", "", "", -999.0);

        var result = validator.Validate(
            new[] { badKql },
            new[] { badRunbook },
            Array.Empty<MemoryCitation>(),
            Array.Empty<DeploymentDiffCitation>());

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }
}
