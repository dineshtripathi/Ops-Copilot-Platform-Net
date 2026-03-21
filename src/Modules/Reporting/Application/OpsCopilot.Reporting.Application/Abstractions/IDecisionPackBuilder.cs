using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Slice 99: Assembles a bounded, safe operator decision pack from the
/// pre-computed safe signals attached to a run detail.
/// No live LLM call, no external I/O, no mutations.
/// </summary>
public interface IDecisionPackBuilder
{
    /// <summary>
    /// Builds a decision pack from pre-computed, tenant-scoped signal data.
    /// Returns null when <paramref name="briefing"/> is null (non-terminal run).
    /// Pure computation — synchronous, no side effects.
    /// </summary>
    OperatorDecisionPack? Build(
        RunBriefing?                         briefing,
        IncidentSynthesis?                   synthesis,
        ServiceBusSignals?                   serviceBus,
        AzureChangeSynthesis?                azureChange,
        ConnectivitySignals?                 connectivity,
        AuthSignals?                         auth,
        IReadOnlyList<SimilarPriorIncident>? priorIncidents,
        IReadOnlyList<ProposedNextAction>?   proposedActions,
        EvidenceQualityAssessment?           quality);
}
