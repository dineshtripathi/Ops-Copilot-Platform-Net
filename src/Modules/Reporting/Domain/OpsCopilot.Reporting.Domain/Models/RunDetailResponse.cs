namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 66/68: Safe read-only projection of a single agent run for the detail page.
/// Contains only summary-safe fields — no raw evidence, no CitationsJson, no prompts.
/// Slice 68 adds evidence-summary metadata (counts/presence flags) derived from the
/// ledger; raw payloads are never included.
/// </summary>
public sealed record RunDetailResponse(
    Guid RunId,
    Guid? SessionId,
    string Status,
    string? AlertFingerprint,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int? TotalTokens,
    decimal? EstimatedCost,
    // Slice 68: evidence-summary — counts/presence flags only, no raw content
    int? ToolCallCount = null,
    int? ToolCallSuccessCount = null,
    int? ToolCallFailedCount = null,
    int? ActionCount = null,
    bool? HasCitations = null,
    // Slice 86: structured triage briefing — counts/flags only, no raw JSON
    RunBriefing? Briefing = null,
    // Slice 88: deterministic next-step hints — no LLM, no raw data
    IReadOnlyList<RunRecommendation>? RunRecommendations = null,
    // Slice 89: deterministic correlated incident view — no LLM, no raw data
    IncidentSynthesis? Synthesis = null,
    // Slice 90: memory-backed similar prior incidents — no LLM, no raw embeddings
    IReadOnlyList<SimilarPriorIncident>? SimilarPriorIncidents = null,
    // Slice 91: Service Bus queue health signals — read-only, no connection strings, no payloads
    ServiceBusSignals? ServiceBusSignals = null,
    // Slice 93: Azure ARM deployment change signals — read-only, no subscription IDs logged
    AzureChangeSynthesis? AzureChangeSynthesis = null,
    // Slice 94: networking/connectivity triage signals — deterministic, no raw error text
    ConnectivitySignals? ConnectivitySignals = null,
    // Slice 95: identity/auth failure signals — deterministic, no secrets/tokens in output
    AuthSignals? AuthSignals = null,
    // Slice 107: governed App Insights / Azure Monitor evidence summary — safe summaries only
    ObservabilityEvidenceSummary? ObservabilityEvidence = null,
    // Slice 97: governed proposal drafting — deterministic only, no LLM, recommendation-only
    IReadOnlyList<ProposedNextAction>? ProposedNextActions = null,
    // Slice 98: evidence quality assessment — deterministic only, no LLM, no raw payloads
    EvidenceQualityAssessment? EvidenceQuality = null,
    // Slice 99: operator decision pack — deterministic, no LLM, no raw payloads
    OperatorDecisionPack? DecisionPack = null);
