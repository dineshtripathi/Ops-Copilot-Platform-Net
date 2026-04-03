using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 99: Deterministic, stateless decision-pack builder.
/// Assembles a safe operator decision pack from already-computed, tenant-scoped signals.
/// No LLM call, no I/O, no mutations.
/// </summary>
internal sealed class DecisionPackBuilder : IDecisionPackBuilder
{
    private const string FallbackGuidance =
        "No evidence available. Manual investigation required.";

    public OperatorDecisionPack? Build(
        RunBriefing?                         briefing,
        IncidentSynthesis?                   synthesis,
        ServiceBusSignals?                   serviceBus,
        AzureChangeSynthesis?                azureChange,
        ConnectivitySignals?                 connectivity,
        AuthSignals?                         auth,
        IReadOnlyList<SimilarPriorIncident>? priorIncidents,
        IReadOnlyList<ProposedNextAction>?   proposedActions,
        EvidenceQualityAssessment?           quality)
    {
        if (briefing is null)
            return null;

        IReadOnlyList<string> actions = proposedActions is { Count: > 0 }
            ? proposedActions.Select(p => p.Proposal).ToList()
            : [];

        var findings = BuildKeyFindings(briefing, priorIncidents);

        return new OperatorDecisionPack(
            IncidentSeverity:   briefing.StatusSeverity,
            IncidentAssessment: synthesis?.OverallAssessment,
            RecommendedActions: actions,
            KeyFindings:        findings,
            EvidenceStrength:   quality?.Strength  ?? EvidenceStrength.Insufficient,
            EvidenceGuidance:   quality?.Guidance   ?? FallbackGuidance,
            GeneratedAt:        DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<string> BuildKeyFindings(
        RunBriefing                          briefing,
        IReadOnlyList<SimilarPriorIncident>? priorIncidents)
    {
        var findings = new List<string>(6);

        if (briefing.FailureSignal is { Length: > 0 } fs)
            findings.Add(fs);

        if (briefing.ToolSuccessRate is < 1.0 and >= 0.0)
            findings.Add($"Tool success rate: {briefing.ToolSuccessRate:P0}");

        if (briefing.KqlRowCount > 0)
            findings.Add($"KQL diagnostics returned {briefing.KqlRowCount} rows");

        if (briefing.RunbookHitCount > 0)
            findings.Add($"{briefing.RunbookHitCount} matching runbook(s) identified");

        if (briefing.MemoryHitCount > 0)
            findings.Add($"{briefing.MemoryHitCount} similar memory item(s) retrieved");

        if (priorIncidents is { Count: > 0 } pi)
            findings.Add($"{pi.Count} similar prior incident(s) found in memory");

        return findings;
    }
}
