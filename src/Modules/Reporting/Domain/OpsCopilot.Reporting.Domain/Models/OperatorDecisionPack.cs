namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 99: Safe, bounded operator decision pack derived deterministically
/// from existing evidence already held in the product.
/// No live LLM call, no raw payloads, read-only — tenant-scoped inputs only.
/// </summary>
public sealed record OperatorDecisionPack(
    string                    IncidentSeverity,
    string?                   IncidentAssessment,
    IReadOnlyList<string>     RecommendedActions,
    IReadOnlyList<string>     KeyFindings,
    EvidenceStrength          EvidenceStrength,
    string                    EvidenceGuidance,
    DateTimeOffset            GeneratedAt,
    string                    PackVersion = "1.0");
