using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Slice 98: Derives a bounded, deterministic evidence-quality assessment from the
/// already-computed safe signals attached to a run detail.
/// No live LLM call, no external I/O, no mutations.
/// </summary>
public interface IEvidenceQualityEvaluator
{
    /// <summary>
    /// Evaluates evidence quality from the pre-computed signal families for a run.
    /// All inputs must already be tenant-scoped before calling this method.
    /// Pure computation — synchronous, no side effects.
    /// </summary>
    EvidenceQualityAssessment Evaluate(
        RunBriefing?                         briefing,
        IncidentSynthesis?                   synthesis,
        ServiceBusSignals?                   serviceBus,
        AzureChangeSynthesis?                azureChange,
        ConnectivitySignals?                 connectivity,
        AuthSignals?                         auth,
        IReadOnlyList<SimilarPriorIncident>? priorIncidents,
        IReadOnlyList<RunRecommendation>?    recommendations);
}
