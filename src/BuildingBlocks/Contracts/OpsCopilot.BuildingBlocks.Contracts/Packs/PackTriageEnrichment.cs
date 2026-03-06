namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// A discovered pack runbook with its content for triage enrichment.
/// </summary>
/// <param name="PackName">Name of the pack this runbook belongs to.</param>
/// <param name="RunbookId">Unique identifier (kebab-case) of the runbook.</param>
/// <param name="File">Relative file path within the pack directory.</param>
/// <param name="ContentSnippet">First portion of the runbook content (may be truncated).</param>
public sealed record PackRunbookDetail(
    string PackName,
    string RunbookId,
    string File,
    string? ContentSnippet);

/// <summary>
/// A discovered pack evidence collector with its KQL content for triage enrichment.
/// </summary>
/// <param name="PackName">Name of the pack this evidence collector belongs to.</param>
/// <param name="EvidenceCollectorId">Unique identifier (kebab-case) of the evidence collector.</param>
/// <param name="RequiredMode">Mode required to run this collector (A, B, or C).</param>
/// <param name="QueryFile">Relative path to the .kql file within the pack directory.</param>
/// <param name="KqlContent">Full content of the .kql file, or null if unreadable.</param>
public sealed record PackEvidenceCollectorDetail(
    string PackName,
    string EvidenceCollectorId,
    string RequiredMode,
    string? QueryFile,
    string? KqlContent);

/// <summary>
/// Result of enriching a triage run with pack data.
/// </summary>
/// <param name="PackRunbooks">Runbooks discovered from Mode-A packs.</param>
/// <param name="PackEvidenceCollectors">Evidence collectors discovered from Mode-A packs.</param>
/// <param name="PackErrors">Non-fatal errors encountered during enrichment.</param>
public sealed record PackTriageEnrichment(
    IReadOnlyList<PackRunbookDetail> PackRunbooks,
    IReadOnlyList<PackEvidenceCollectorDetail> PackEvidenceCollectors,
    IReadOnlyList<string> PackErrors);
