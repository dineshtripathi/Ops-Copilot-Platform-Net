using System.Text.Json;

namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>Serialised form of a KQL citation returned to callers.</summary>
/// <param name="WorkspaceId">Log Analytics workspace that was queried.</param>
/// <param name="ExecutedQuery">Exact KQL that was run.</param>
/// <param name="Timespan">ISO 8601 duration used as the query time range.</param>
/// <param name="ExecutedAtUtc">UTC timestamp when the query was executed.</param>
public sealed record CitationDto(
    string         WorkspaceId,
    string         ExecutedQuery,
    string         Timespan,
    DateTimeOffset ExecutedAtUtc);

/// <summary>Serialised form of a runbook citation returned to callers.</summary>
/// <param name="RunbookId">Unique identifier of the matched runbook.</param>
/// <param name="Title">Title of the runbook.</param>
/// <param name="Snippet">Relevant content snippet from the runbook.</param>
/// <param name="Score">Relevance score (0.0 – 1.0).</param>
public sealed record RunbookCitationDto(
    string RunbookId,
    string Title,
    string Snippet,
    double Score);

/// <summary>Pack runbook detail surfaced from the Packs module during triage enrichment.</summary>
/// <param name="PackName">Name of the pack that owns the runbook.</param>
/// <param name="RunbookId">Identifier of the runbook within the pack.</param>
/// <param name="File">Relative file path inside the pack.</param>
/// <param name="ContentSnippet">Truncated content of the runbook file (may be null if unreadable).</param>
public sealed record PackRunbookDto(
    string  PackName,
    string  RunbookId,
    string  File,
    string? ContentSnippet);

/// <summary>Pack evidence-collector detail surfaced from the Packs module during triage enrichment.</summary>
/// <param name="PackName">Name of the pack that owns the evidence collector.</param>
/// <param name="EvidenceCollectorId">Identifier of the evidence collector within the pack.</param>
/// <param name="RequiredMode">Mode required to execute this collector (e.g. "A").</param>
/// <param name="QueryFile">Relative path to the KQL query file (may be null).</param>
/// <param name="KqlContent">Full KQL content of the query file (may be null if unreadable).</param>
public sealed record PackEvidenceCollectorDto(
    string  PackName,
    string  EvidenceCollectorId,
    string  RequiredMode,
    string? QueryFile,
    string? KqlContent);

/// <summary>Per-proposal outcome of the Mode C safe-action recording step.</summary>
/// <param name="PackName">The pack that owns this action.</param>
/// <param name="ActionId">Unique action identifier within the pack.</param>
/// <param name="ActionType">The type of action (e.g. run-command).</param>
/// <param name="ActionRecordId">The persisted SafeAction record identifier, or null on failure/skip.</param>
/// <param name="Status">Outcome status: Created, Skipped, PolicyDenied, or Failed.</param>
/// <param name="ErrorMessage">Non-null when the record could not be created.</param>
/// <param name="PolicyDenialReasonCode">Non-null when the proposal was denied by SafeAction policy.</param>
public sealed record PackSafeActionRecordItemDto(
    string  PackName,
    string  ActionId,
    string  ActionType,
    Guid?   ActionRecordId,
    string  Status,
    string? ErrorMessage,
    string? PolicyDenialReasonCode);

/// <summary>Summary of the Mode C safe-action recording step (null when mode is not C or feature is disabled).</summary>
/// <param name="Records">Per-proposal recording outcomes.</param>
/// <param name="CreatedCount">Number of proposals that were successfully recorded.</param>
/// <param name="SkippedCount">Number of proposals that were skipped (not executable / governance denied).</param>
/// <param name="FailedCount">Number of proposals that failed (policy denied or unexpected error).</param>
/// <param name="Errors">Aggregate error messages encountered during recording.</param>
public sealed record PackSafeActionRecordSummaryDto(
    IReadOnlyList<PackSafeActionRecordItemDto> Records,
    int                                        CreatedCount,
    int                                        SkippedCount,
    int                                        FailedCount,
    IReadOnlyList<string>                      Errors);

/// <summary>Response body for POST /agent/triage.</summary>
/// <param name="RunId">Unique identifier of the persisted AgentRun ledger entry.</param>
/// <param name="Status">Terminal status of the run (Completed / Degraded / Failed).</param>
/// <param name="Summary">Structured JSON summary (e.g. <c>{"rowCount":5}</c>). Null on failure.</param>
/// <param name="Citations">Evidence citations — one per KQL tool invocation.</param>
/// <param name="RunbookCitations">Runbook search citations — one per matched runbook.</param>
/// <param name="PackRunbooks">Pack runbook details discovered during triage enrichment (Mode A).</param>
/// <param name="PackEvidenceCollectors">Pack evidence-collector details discovered during triage enrichment (Mode A).</param>
/// <param name="PackErrors">Non-fatal errors encountered during pack enrichment (null when none).</param>
/// <param name="PackSafeActionRecordSummary">Summary of Mode C safe-action recording (null when mode ≠ C or feature disabled).</param>
public sealed record TriageResponse(
    Guid                                       RunId,
    string                                     Status,
    JsonElement?                               Summary,
    IReadOnlyList<CitationDto>                 Citations,
    IReadOnlyList<RunbookCitationDto>          RunbookCitations,
    Guid?                                      SessionId,
    bool                                       IsNewSession,
    DateTimeOffset?                            SessionExpiresAtUtc,
    bool                                       UsedSessionContext,
    IReadOnlyList<PackRunbookDto>?             PackRunbooks                = null,
    IReadOnlyList<PackEvidenceCollectorDto>?   PackEvidenceCollectors      = null,
    IReadOnlyList<string>?                     PackErrors                  = null,
    IReadOnlyList<PackEvidenceResultDto>?      PackEvidenceResults         = null,
    IReadOnlyList<PackSafeActionProposalDto>?  PackSafeActionProposals     = null,
    PackSafeActionRecordSummaryDto?            PackSafeActionRecordSummary = null);
