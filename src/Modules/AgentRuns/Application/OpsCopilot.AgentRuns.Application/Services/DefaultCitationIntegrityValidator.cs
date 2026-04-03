using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Application.Services;

/// <summary>
/// Slice 173 — MVP guardrail §8.1: Citation Integrity Validator.
///
/// Rules (deterministic, no LLM):
///  KQL citations   — WorkspaceId and ExecutedQuery must be non-empty.
///  Runbook         — RunbookId and Title must be non-empty; Score must be ≥ 0.
///  Memory          — RunId and AlertFingerprint must be non-empty; Score must be ≥ 0.
///  DeploymentDiff  — SubscriptionId, ResourceId, and ChangeType must be non-empty;
///                    ChangeTime must be a real UTC timestamp (not default).
/// </summary>
public sealed class DefaultCitationIntegrityValidator : ICitationValidator
{
    public CitationValidationResult Validate(
        IReadOnlyList<KqlCitation>            kqlCitations,
        IReadOnlyList<RunbookCitation>        runbookCitations,
        IReadOnlyList<MemoryCitation>         memoryCitations,
        IReadOnlyList<DeploymentDiffCitation> deploymentDiffCitations)
    {
        var violations = new List<string>();

        for (var i = 0; i < kqlCitations.Count; i++)
        {
            var c = kqlCitations[i];
            if (string.IsNullOrWhiteSpace(c.WorkspaceId))
                violations.Add($"KqlCitation[{i}]: WorkspaceId is empty.");
            if (string.IsNullOrWhiteSpace(c.ExecutedQuery))
                violations.Add($"KqlCitation[{i}]: ExecutedQuery is empty.");
        }

        for (var i = 0; i < runbookCitations.Count; i++)
        {
            var c = runbookCitations[i];
            if (string.IsNullOrWhiteSpace(c.RunbookId))
                violations.Add($"RunbookCitation[{i}]: RunbookId is empty.");
            if (string.IsNullOrWhiteSpace(c.Title))
                violations.Add($"RunbookCitation[{i}]: Title is empty.");
            if (c.Score < 0)
                violations.Add($"RunbookCitation[{i}]: Score is negative ({c.Score}).");
        }

        for (var i = 0; i < memoryCitations.Count; i++)
        {
            var c = memoryCitations[i];
            if (string.IsNullOrWhiteSpace(c.RunId))
                violations.Add($"MemoryCitation[{i}]: RunId is empty.");
            if (string.IsNullOrWhiteSpace(c.AlertFingerprint))
                violations.Add($"MemoryCitation[{i}]: AlertFingerprint is empty.");
            if (c.Score < 0)
                violations.Add($"MemoryCitation[{i}]: Score is negative ({c.Score}).");
        }

        for (var i = 0; i < deploymentDiffCitations.Count; i++)
        {
            var c = deploymentDiffCitations[i];
            if (string.IsNullOrWhiteSpace(c.SubscriptionId))
                violations.Add($"DeploymentDiffCitation[{i}]: SubscriptionId is empty.");
            if (string.IsNullOrWhiteSpace(c.ResourceId))
                violations.Add($"DeploymentDiffCitation[{i}]: ResourceId is empty.");
            if (string.IsNullOrWhiteSpace(c.ChangeType))
                violations.Add($"DeploymentDiffCitation[{i}]: ChangeType is empty.");
            if (c.ChangeTime == default)
                violations.Add($"DeploymentDiffCitation[{i}]: ChangeTime is default (unset).");
        }

        return violations.Count == 0
            ? CitationValidationResult.Pass()
            : CitationValidationResult.Fail(violations);
    }
}
