using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Application.Services;

/// <summary>
/// No-op citation validator — always returns Pass.
/// Used in tests and development scenarios where citation validation is not needed.
/// </summary>
public sealed class NullCitationValidator : ICitationValidator
{
    public CitationValidationResult Validate(
        IReadOnlyList<KqlCitation>            kqlCitations,
        IReadOnlyList<RunbookCitation>        runbookCitations,
        IReadOnlyList<MemoryCitation>         memoryCitations,
        IReadOnlyList<DeploymentDiffCitation> deploymentDiffCitations)
        => CitationValidationResult.Pass();
}
