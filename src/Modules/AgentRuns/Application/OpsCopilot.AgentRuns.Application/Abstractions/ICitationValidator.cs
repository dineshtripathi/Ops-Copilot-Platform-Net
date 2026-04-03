namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Validates that every citation in a triage result has a non-empty, attributable source.
/// This is an MVP guardrail (§8.1): no citation without evidence provenance may reach the ledger.
/// </summary>
public interface ICitationValidator
{
    /// <summary>
    /// Returns a <see cref="CitationValidationResult"/> describing whether all citations pass
    /// the integrity check, and which ones failed.
    /// </summary>
    CitationValidationResult Validate(
        IReadOnlyList<KqlCitation> kqlCitations,
        IReadOnlyList<RunbookCitation> runbookCitations,
        IReadOnlyList<MemoryCitation> memoryCitations,
        IReadOnlyList<DeploymentDiffCitation> deploymentDiffCitations);
}

/// <summary>
/// Result of a citation integrity check. Immutable value object.
/// </summary>
public sealed record CitationValidationResult(
    bool IsValid,
    IReadOnlyList<string> Violations)
{
    /// <summary>Convenience — passes with zero violations.</summary>
    public static CitationValidationResult Pass()
        => new(true, Array.Empty<string>());

    /// <summary>Convenience — fails with a list of violation messages.</summary>
    public static CitationValidationResult Fail(IReadOnlyList<string> violations)
        => new(false, violations);
}
