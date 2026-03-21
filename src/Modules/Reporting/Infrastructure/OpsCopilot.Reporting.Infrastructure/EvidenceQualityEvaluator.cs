using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 98: Deterministic, stateless evidence-quality evaluator.
/// Counts signal families already populated by earlier slices.
/// No LLM call, no I/O, no mutations.
/// </summary>
internal sealed class EvidenceQualityEvaluator : IEvidenceQualityEvaluator
{
    private const int TotalFamilies = 8;

    private static readonly IReadOnlyDictionary<EvidenceStrength, string> Guidance =
        new Dictionary<EvidenceStrength, string>
        {
            [EvidenceStrength.Strong]       = "Evidence is comprehensive. Proceed with confidence.",
            [EvidenceStrength.Moderate]     = "Evidence is sufficient for initial triage. Consider filling gaps before executing changes.",
            [EvidenceStrength.Weak]         = "Evidence is limited. Gather additional signal families before acting.",
            [EvidenceStrength.Insufficient] = "No signal families are available. Manual investigation is required."
        };

    public EvidenceQualityAssessment Evaluate(
        RunBriefing?                         briefing,
        IncidentSynthesis?                   synthesis,
        ServiceBusSignals?                   serviceBus,
        AzureChangeSynthesis?                azureChange,
        ConnectivitySignals?                 connectivity,
        AuthSignals?                         auth,
        IReadOnlyList<SimilarPriorIncident>? priorIncidents,
        IReadOnlyList<RunRecommendation>?    recommendations)
    {
        var missing = new List<string>(TotalFamilies);

        int present = 0;
        present += Count(briefing         is not null,                          "Triage Briefing",          missing);
        present += Count(synthesis        is not null,                          "Incident Correlation",     missing);
        present += Count(serviceBus       is not null,                          "Service Bus Signals",      missing);
        present += Count(azureChange      is not null,                          "Azure Change Signals",     missing);
        present += Count(connectivity     is not null,                          "Connectivity Signals",     missing);
        present += Count(auth             is not null,                          "Auth Signals",             missing);
        present += Count(priorIncidents   is { Count: > 0 },                   "Similar Prior Incidents",  missing);
        present += Count(recommendations  is { Count: > 0 },                   "Run Recommendations",      missing);

        var strength     = ClassifyStrength(present);
        var completeness = ClassifyCompleteness(present);

        return new EvidenceQualityAssessment(
            Strength:              strength,
            Completeness:          completeness,
            SignalFamiliesPresent: present,
            SignalFamiliesTotal:   TotalFamilies,
            MissingAreas:          missing,
            Guidance:              Guidance[strength],
            EvaluatedAt:           DateTimeOffset.UtcNow);
    }

    // Returns 1 if present, 0 if absent; appends missing area label when absent.
    private static int Count(bool isPresent, string label, List<string> missing)
    {
        if (isPresent) return 1;
        missing.Add(label);
        return 0;
    }

    private static EvidenceStrength ClassifyStrength(int present) => present switch
    {
        >= 5 => EvidenceStrength.Strong,
        >= 3 => EvidenceStrength.Moderate,
        >= 1 => EvidenceStrength.Weak,
        _    => EvidenceStrength.Insufficient
    };

    private static EvidenceCompleteness ClassifyCompleteness(int present) => present switch
    {
        TotalFamilies => EvidenceCompleteness.Complete,
        >= 3          => EvidenceCompleteness.Partial,
        _             => EvidenceCompleteness.Sparse
    };
}
